using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Util
{
    public class PlaywrightHttpOptions
    {
        /// <summary>Run Chromium without a window. Turn it off to watch the crawl.</summary>
        public bool Headless { get; set; } = true;

        /// <summary>Download Chromium on the first request if it is missing (same as <c>playwright install chromium</c>).</summary>
        public bool InstallBrowser { get; set; } = true;

        /// <summary>User-Agent of the browser; <c>null</c> uses <see cref="Common.UserAgent"/>.</summary>
        public string? UserAgent { get; set; }

        /// <summary>
        /// Recognises an interstitial page (bot check) that runs a script and then loads the requested page.
        /// When it matches, the handler waits up to <see cref="ChallengeTimeout"/> for the next page and returns that one.
        /// </summary>
        public Func<string, bool>? IsChallenge { get; set; } = SmogonPages.IsBotCheck;

        /// <summary>How long a bot check may take before its page is returned as it is.</summary>
        public TimeSpan ChallengeTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Sends GET requests through a headless Chromium (Playwright): every request opens the URL in a new tab of one
    /// shared browser context, so cookies work like in a normal browser, and bot checks that solve themselves with a
    /// script (Smogon's proof-of-work gate) are passed.
    /// The response carries the HTML exactly as the server sent it, not the DOM after scripts changed it. The tab is
    /// closed as soon as that HTML is in, so only a bot check gets to run its script; images are never loaded.
    /// </summary>
    public sealed class PlaywrightHttpMessageHandler : HttpMessageHandler, IAsyncDisposable
    {
        private readonly PlaywrightHttpOptions _options;
        private readonly SemaphoreSlim _startLock = new(1, 1);
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private bool _disposed;

        public PlaywrightHttpMessageHandler() : this(null) { }

        public PlaywrightHttpMessageHandler(PlaywrightHttpOptions? options)
        {
            _options = options ?? new PlaywrightHttpOptions();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
            {
                throw new NotSupportedException($"{nameof(PlaywrightHttpMessageHandler)} only sends GET requests, not {request.Method}.");
            }
            var url = request.RequestUri?.AbsoluteUri ?? throw new InvalidOperationException("The request has no URI.");

            // Errors while starting the browser are not wrapped in HttpRequestException: retrying would not fix them.
            var context = await StartAsync(cancellationToken).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            try
            {
                return await LoadAsync(page, request, url, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException e) when (!cancellationToken.IsCancellationRequested)
            {
                // net::ERR_CONNECTION_RESET, ERR_NAME_NOT_RESOLVED, ... (Playwright's TimeoutException is one as well)
                throw new HttpRequestException($"{e.Message.Split('\n')[0]} ({url})", e);
            }
            finally
            {
                // Also stops what the page still loads, and a navigation that was cancelled.
                try
                {
                    await page.CloseAsync().ConfigureAwait(false);
                }
                catch (PlaywrightException)
                {
                    // Browser already gone; the original error (if any) is more useful.
                }
            }
        }

        private async Task<HttpResponseMessage> LoadAsync(IPage page, HttpRequestMessage request, string url, CancellationToken cancellationToken)
        {
            var documents = new DocumentTracker();
            page.Response += (_, response) =>
            {
                try
                {
                    if (response.Request.IsNavigationRequest && response.Frame == page.MainFrame && !IsRedirect(response.Status))
                    {
                        documents.Add(response);
                    }
                }
                catch (PlaywrightException)
                {
                    // Response without frame (service worker)
                }
            };

            // Returns once the headers are in. The body is read as soon as it is complete, without waiting for the page's
            // scripts and stylesheets: only a bot check needs its script to run, and the tab stays open for that.
            var current = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 0 })
                .WaitAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new HttpRequestException($"The browser received no response for {url}");
            var challengeDeadline = DateTime.UtcNow + _options.ChallengeTimeout;

            while (true)
            {
                // A bot check can reload the page before its own body is complete. That body never arrives
                // (Playwright's BodyAsync does not return), so the next document ends the wait as well.
                var bodyTask = current.BodyAsync();
                var newer = await documents.NewerThanAsync(current, bodyTask, null, cancellationToken).ConfigureAwait(false);
                if (newer != null)
                {
                    Ignore(bodyTask);
                    current = newer;
                    continue;
                }

                byte[] body;
                try
                {
                    body = await bodyTask.ConfigureAwait(false);
                }
                catch (PlaywrightException)
                {
                    // The page navigated on right when the body was read; that next document is already on its way.
                    newer = await documents.NewerThanAsync(current, null, Min(challengeDeadline - DateTime.UtcNow, TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
                    current = newer ?? throw new HttpRequestException($"The browser could not read the page {url}");
                    continue;
                }

                if (!IsChallenge(current, body))
                {
                    return await ToHttpResponseAsync(request, current, body).ConfigureAwait(false);
                }

                newer = await documents.NewerThanAsync(current, null, challengeDeadline - DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                if (newer == null)
                {
                    // The bot check did not finish; the caller decides what to do with it.
                    return await ToHttpResponseAsync(request, current, body).ConfigureAwait(false);
                }
                current = newer;
            }
        }

        private static void Ignore(Task task) => task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        /// <summary>The documents (main frame responses after redirects) a tab loaded, newest last.</summary>
        private sealed class DocumentTracker
        {
            private readonly object _lock = new();
            private IResponse? _latest;
            private TaskCompletionSource _added = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Add(IResponse response)
            {
                TaskCompletionSource added;
                lock (_lock)
                {
                    _latest = response;
                    added = _added;
                    _added = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                added.TrySetResult();
            }

            /// <summary>
            /// Waits for a document loaded after <paramref name="current"/>. Returns null once <paramref name="until"/>
            /// completes or the timeout (null: none) passes first.
            /// </summary>
            public async Task<IResponse?> NewerThanAsync(IResponse current, Task? until, TimeSpan? timeout, CancellationToken cancellationToken)
            {
                using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeout != null)
                {
                    timer.CancelAfter(timeout.Value > TimeSpan.Zero ? timeout.Value : TimeSpan.Zero);
                }
                var expired = Task.Delay(Timeout.Infinite, timer.Token);
                while (true)
                {
                    Task added;
                    lock (_lock)
                    {
                        // The response of GotoAsync itself may be reported after it returned; it is not newer.
                        if (_latest != null && !ReferenceEquals(_latest, current))
                        {
                            return _latest;
                        }
                        added = _added.Task;
                    }
                    var done = await Task.WhenAny(until == null ? [added, expired] : [added, until, expired]).ConfigureAwait(false);
                    if (done == expired)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return null;
                    }
                    if (done == until)
                    {
                        return null;
                    }
                }
            }
        }

        private bool IsChallenge(IResponse response, byte[] body) =>
            _options.IsChallenge != null
            && response.Headers.TryGetValue("content-type", out var contentType)
            && contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
            && _options.IsChallenge(Encoding.UTF8.GetString(body));

        private static bool IsRedirect(int status) => status is 301 or 302 or 303 or 307 or 308;

        private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        /// <summary>Headers that describe the transfer, not the body Playwright hands over (already decompressed).</summary>
        private static readonly HashSet<string> TransferHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "content-encoding", "content-length", "transfer-encoding",
        };

        private static async Task<HttpResponseMessage> ToHttpResponseAsync(HttpRequestMessage request, IResponse response, byte[] body)
        {
            // The browser revalidated its cached copy. The caller did not ask for a conditional request, so it gets the page.
            var status = response.Status == 304 ? HttpStatusCode.OK : (HttpStatusCode)response.Status;
            var message = new HttpResponseMessage(status)
            {
                RequestMessage = request,
                ReasonPhrase = status == HttpStatusCode.OK ? "OK" : response.StatusText,
                Content = new ByteArrayContent(body),
            };
            foreach (var header in await response.HeadersArrayAsync().ConfigureAwait(false))
            {
                if (TransferHeaders.Contains(header.Name))
                {
                    continue;
                }
                if (!message.Headers.TryAddWithoutValidation(header.Name, header.Value))
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
                }
            }
            return message;
        }

        /// <summary>Starts the browser; called by the first request. Call it before to fail early when Chromium cannot start.</summary>
        public async Task<IBrowserContext> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_context != null)
            {
                return _context;
            }
            await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_context != null)
                {
                    return _context;
                }

                if (_options.InstallBrowser)
                {
                    // Does nothing when the browser of this Playwright version is already installed.
                    var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"])).ConfigureAwait(false);
                    if (exitCode != 0)
                    {
                        throw new InvalidOperationException($"Installing Chromium for Playwright failed (exit code {exitCode}), see https://playwright.dev/dotnet/docs/browsers");
                    }
                }

                _playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _options.Headless,
                    // Unlike request interception (page.route), this keeps the HTTP cache working.
                    Args = ["--blink-settings=imagesEnabled=false"],
                }).ConfigureAwait(false);
                var context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = _options.UserAgent ?? Common.UserAgent,
                }).ConfigureAwait(false);
                // Attempt timeouts come from the CancellationToken (see PoliteHttpOptions.RequestTimeout).
                context.SetDefaultNavigationTimeout(0);
                _context = context;
                return context;
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseBrowserAsync().ConfigureAwait(false);
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseBrowserAsync().GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }

        private async Task CloseBrowserAsync()
        {
            await _startLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                if (_context != null)
                {
                    await _context.CloseAsync().ConfigureAwait(false);
                }
                if (_browser != null)
                {
                    await _browser.CloseAsync().ConfigureAwait(false);
                }
                _playwright?.Dispose();
                _context = null;
                _browser = null;
                _playwright = null;
            }
            finally
            {
                _startLock.Release();
            }
        }
    }
}
