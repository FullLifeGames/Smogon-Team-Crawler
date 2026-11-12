using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Util
{
    public class PoliteHttpOptions
    {
        /// <summary>Minimum pause between two requests to the same host.</summary>
        public TimeSpan MinDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>How often a request is retried after a timeout, a network error or a 408/429/5xx answer.</summary>
        public int MaxRetries { get; set; } = 4;

        /// <summary>Backoff before the first retry when the server sends no Retry-After; it triples on every further retry.</summary>
        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Longest wait the handler accepts before a retry. If the server asks for more (via Retry-After),
        /// the handler gives up and returns the error response instead of retrying.
        /// </summary>
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Timeout of a single attempt. Use it instead of <see cref="HttpClient.Timeout"/>, which would also count the waits
        /// between requests and retries (see <see cref="PoliteHttpMessageHandler.CreateClient"/>).
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);
    }

    /// <summary>
    /// Sends at most one request per host at a time, waits <see cref="PoliteHttpOptions.MinDelay"/> between them,
    /// honours Retry-After, retries only transient failures and identifies itself with a User-Agent.
    /// By default the requests go through a headless browser (<see cref="PlaywrightHttpMessageHandler"/>).
    /// </summary>
    public class PoliteHttpMessageHandler : DelegatingHandler
    {
        private sealed class HostSlot
        {
            public readonly SemaphoreSlim Lock = new(1, 1);
            public DateTimeOffset NextAllowed = DateTimeOffset.MinValue;
        }

        private readonly PoliteHttpOptions _options;
        private readonly ConcurrentDictionary<string, HostSlot> _slots = new();

        public RequestStatistics Statistics { get; } = new();

        public PoliteHttpMessageHandler() : this(null, null) { }

        public PoliteHttpMessageHandler(PoliteHttpOptions? options) : this(options, null) { }

        public PoliteHttpMessageHandler(PoliteHttpOptions? options, HttpMessageHandler? innerHandler)
            : base(innerHandler ?? CreateDefaultInnerHandler())
        {
            _options = options ?? new PoliteHttpOptions();
        }

        /// <summary>
        /// An HttpClient that leaves attempt timeouts to the handler, so that waits and retries are not cut short.
        /// Its own timeout only caps a request including all retries (and reading the body) against hanging forever.
        /// </summary>
        public static HttpClient CreateClient(PoliteHttpMessageHandler handler) => new(handler) { Timeout = TimeSpan.FromMinutes(30) };

        /// <summary>
        /// Requests go through a headless browser (see <see cref="PlaywrightHttpMessageHandler"/>).
        /// Pass a <see cref="SocketsHttpHandler"/> as inner handler for plain HTTP requests.
        /// </summary>
        private static HttpMessageHandler CreateDefaultInnerHandler() => new PlaywrightHttpMessageHandler();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.UserAgent.Any())
            {
                request.Headers.TryAddWithoutValidation("User-Agent", Common.UserAgent);
            }

            var host = request.RequestUri?.Host ?? "";
            var slot = _slots.GetOrAdd(host, _ => new HostSlot());

            for (var attempt = 0; ; attempt++)
            {
                HttpResponseMessage? response = null;
                ExceptionDispatchInfo? error = null;

                await slot.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var wait = slot.NextAllowed - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    }

                    Statistics.Record(host);
                    using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    attemptTimeout.CancelAfter(_options.RequestTimeout);
                    try
                    {
                        response = await base.SendAsync(request, attemptTimeout.Token).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                    {
                        error = ExceptionDispatchInfo.Capture(e);
                    }
                    catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Timeout of this attempt
                        error = ExceptionDispatchInfo.Capture(e);
                    }
                    slot.NextAllowed = DateTimeOffset.UtcNow + _options.MinDelay;
                }
                finally
                {
                    slot.Lock.Release();
                }

                if (response != null && !IsTransient(response.StatusCode))
                {
                    return response;
                }

                var delay = GetRetryDelay(response, attempt);
                if (attempt >= _options.MaxRetries || delay > _options.MaxBackoff)
                {
                    if (response != null)
                    {
                        return response;
                    }
                    error!.Throw();
                }

                Console.WriteLine($"{(response != null ? "HTTP " + (int)response.StatusCode : error!.SourceException.GetType().Name + " (" + error.SourceException.Message + ")")} for {request.RequestUri}, retry {attempt + 1}/{_options.MaxRetries} in {delay.TotalSeconds:0}s");
                response?.Dispose();

                // Every other request to this host waits as well.
                await slot.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var retryAt = DateTimeOffset.UtcNow + delay;
                    if (retryAt > slot.NextAllowed)
                    {
                        slot.NextAllowed = retryAt;
                    }
                }
                finally
                {
                    slot.Lock.Release();
                }
            }
        }

        private static bool IsTransient(HttpStatusCode statusCode) => statusCode
            is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

        private TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt)
        {
            var retryAfter = response?.Headers.RetryAfter;
            if (retryAfter?.Delta is TimeSpan delta)
            {
                return delta;
            }
            if (retryAfter?.Date is DateTimeOffset date)
            {
                var untilDate = date - DateTimeOffset.UtcNow;
                return untilDate > TimeSpan.Zero ? untilDate : TimeSpan.Zero;
            }
            return _options.InitialBackoff * Math.Pow(3, attempt);
        }
    }

    public class RequestStatistics
    {
        private readonly ConcurrentDictionary<string, int> _requestsByHost = new();

        public int Total => _requestsByHost.Values.Sum();

        public int For(string host) => _requestsByHost.TryGetValue(host, out var count) ? count : 0;

        internal void Record(string host) => _requestsByHost.AddOrUpdate(host, 1, (_, count) => count + 1);

        public override string ToString() => "HTTP requests: " + Total
            + (_requestsByHost.IsEmpty ? "" : " (" + string.Join(", ", _requestsByHost.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + ": " + kv.Value)) + ")");
    }
}
