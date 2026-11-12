#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    /// <summary>
    /// Runs Chromium against a local server. Explicit, since the first run downloads the browser;
    /// run them with: dotnet test --filter TestCategory=Browser
    /// </summary>
    [TestFixture]
    [Explicit("Starts Chromium (downloaded on the first run)")]
    [Category("Browser")]
    public class PlaywrightHttpMessageHandlerTests
    {
        private const string ForumPage = "<!DOCTYPE html>\n<html data-template=\"forum_list\">\n<body>\n<p>real page</p>\n</body>\n</html>";

        private HttpListener _listener = null!;
        private string _baseUrl = null!;
        private PlaywrightHttpMessageHandler _browser = null!;
        private HttpClient _client = null!;
        private readonly ConcurrentQueue<string> _requests = new();

        [OneTimeSetUp]
        public async Task StartServerAndBrowser()
        {
            var port = FreePort();
            _baseUrl = $"http://localhost:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _ = Task.Run(ServeAsync);

            _browser = new PlaywrightHttpMessageHandler(new PlaywrightHttpOptions { ChallengeTimeout = TimeSpan.FromSeconds(10) });
            await _browser.StartAsync();
            _client = new HttpClient(_browser);
        }

        [OneTimeTearDown]
        public void Stop()
        {
            _client.Dispose();
            _listener.Close();
        }

        [SetUp]
        public void ClearRequests() => _requests.Clear();

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }
                _ = Task.Run(() => Answer(context));
            }
        }

        private void Answer(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url!.AbsolutePath;
            if (path == "/favicon.ico")
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }
            _requests.Enqueue(path);

            string body;
            var contentType = "text/html; charset=utf-8";
            var passedGate = (request.Headers["Cookie"] ?? "").Contains("gate=passed");
            switch (path)
            {
                case "/page":
                    body = ForumPage;
                    break;
                case "/umlauts":
                    body = "<html><body>Flabébé, Pokémon</body></html>";
                    break;
                case "/paste.txt":
                    contentType = "text/plain; charset=utf-8";
                    body = "Garchomp @ Choice Scarf\nAbility: Rough Skin";
                    break;
                case "/missing":
                    response.StatusCode = 404;
                    body = "<html><body>not here</body></html>";
                    break;
                case "/busy":
                    response.StatusCode = 429;
                    response.AddHeader("Retry-After", "7");
                    body = "<html><body>slow down</body></html>";
                    break;
                case "/redirect":
                    response.StatusCode = 302;
                    response.RedirectLocation = "/page";
                    body = "";
                    break;
                case "/cached":
                    response.AddHeader("ETag", "\"v1\"");
                    response.AddHeader("Cache-Control", "no-cache");
                    if (request.Headers["If-None-Match"] == "\"v1\"")
                    {
                        _requests.Enqueue("304");
                        response.StatusCode = 304;
                        response.Close();
                        return;
                    }
                    body = ForumPage;
                    break;
                case "/gate-delayed":
                case "/gate-immediate":
                    if (passedGate)
                    {
                        body = ForumPage;
                        break;
                    }
                    var delay = path == "/gate-delayed" ? 1500 : 0;
                    body = "<html><head><title>xf_smogon_gate</title></head><body>Checking your browser\n"
                        + "<script>setTimeout(function () { document.cookie = 'gate=passed; path=/'; location.reload(); }, " + delay + ");</script>"
                        + "</body></html>";
                    break;
                case "/gate-before-body":
                    if (passedGate)
                    {
                        body = ForumPage;
                        break;
                    }
                    // The script reloads the page while the rest of the body is still on its way.
                    response.ContentType = contentType;
                    response.SendChunked = true;
                    response.OutputStream.Write(Encoding.UTF8.GetBytes(
                        "<html><body>xf_smogon_gate<script>document.cookie = 'gate=passed; path=/'; location.reload();</script>"));
                    response.OutputStream.Flush();
                    System.Threading.Thread.Sleep(1000);
                    try
                    {
                        response.OutputStream.Write(Encoding.UTF8.GetBytes(new string('x', 10000) + "</body></html>"));
                        response.Close();
                    }
                    catch (HttpListenerException)
                    {
                        // The browser left already
                    }
                    return;
                case "/gate-stuck":
                    body = "<html><body>xf_smogon_gate Checking your browser</body></html>";
                    break;
                default:
                    response.StatusCode = 500;
                    body = "unknown path";
                    break;
            }
            response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
            response.Close();
        }

        private void ResetGateCookie() => _browser.StartAsync().Result.ClearCookiesAsync().Wait();

        [Test]
        public async Task Page_IsReturnedAsSent()
        {
            var response = await _client.GetAsync(_baseUrl + "page");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(ForumPage));
        }

        [Test]
        public async Task Utf8_IsDecoded()
        {
            Assert.That(await _client.GetStringAsync(_baseUrl + "umlauts"), Does.Contain("Flabébé, Pokémon"));
        }

        [Test]
        public async Task PlainText_IsReturned()
        {
            Assert.That(await _client.GetStringAsync(_baseUrl + "paste.txt"), Is.EqualTo("Garchomp @ Choice Scarf\nAbility: Rough Skin"));
        }

        [Test]
        public async Task NotFound_KeepsStatusCode()
        {
            var e = Assert.ThrowsAsync<HttpRequestException>(() => _client.GetStringAsync(_baseUrl + "missing"));
            Assert.That(e!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            await Task.CompletedTask;
        }

        [Test]
        public async Task TooManyRequests_KeepsRetryAfter()
        {
            var response = await _client.GetAsync(_baseUrl + "busy");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(7)));
        }

        [Test]
        public async Task Redirect_IsFollowed()
        {
            Assert.That(await _client.GetStringAsync(_baseUrl + "redirect"), Is.EqualTo(ForumPage));
            Assert.That(_requests, Is.EqualTo(new[] { "/redirect", "/page" }));
        }

        [Test]
        public async Task RevalidatedPage_IsReturnedAsOk()
        {
            Assert.That(await _client.GetStringAsync(_baseUrl + "cached"), Is.EqualTo(ForumPage));
            Assert.That(await _client.GetStringAsync(_baseUrl + "cached"), Is.EqualTo(ForumPage));
            Assert.That(_requests, Is.EqualTo(new[] { "/cached", "/cached", "304" }), "the browser should have revalidated its copy");
        }

        [TestCase("gate-delayed")]
        [TestCase("gate-immediate")]
        [TestCase("gate-before-body")]
        public async Task BotCheck_ThatSolvesItself_ReturnsTheRealPage(string path)
        {
            ResetGateCookie();

            var html = await _client.GetStringAsync(_baseUrl + path);

            Assert.That(html, Is.EqualTo(ForumPage));
            Assert.That(_requests, Is.EqualTo(new[] { "/" + path, "/" + path }));

            // The cookie is kept: the next request gets the page right away.
            _requests.Clear();
            Assert.That(await _client.GetStringAsync(_baseUrl + path), Is.EqualTo(ForumPage));
            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BotCheck_ThatDoesNotFinish_IsReturnedAfterTheTimeout()
        {
            var browser = new PlaywrightHttpMessageHandler(new PlaywrightHttpOptions { ChallengeTimeout = TimeSpan.FromSeconds(1) });
            using var client = new HttpClient(browser);

            var html = await client.GetStringAsync(_baseUrl + "gate-stuck");

            Assert.That(html, Does.Contain("xf_smogon_gate"));
        }

        [Test]
        public void ConnectionError_IsHttpRequestException()
        {
            Assert.ThrowsAsync<HttpRequestException>(() => _client.GetAsync($"http://localhost:{FreePort()}/"));
        }

        [Test]
        public async Task PoliteHandler_UsesTheBrowserByDefault()
        {
            using var client = PoliteHttpMessageHandler.CreateClient(new PoliteHttpMessageHandler(new PoliteHttpOptions { MinDelay = TimeSpan.Zero }));

            Assert.That(await client.GetStringAsync(_baseUrl + "page"), Is.EqualTo(ForumPage));
        }

        [Test]
        public async Task ParallelRequests_AllSucceed()
        {
            var tasks = new List<Task<string>>();
            for (var i = 0; i < 5; i++)
            {
                tasks.Add(_client.GetStringAsync(_baseUrl + "page"));
            }
            foreach (var html in await Task.WhenAll(tasks))
            {
                Assert.That(html, Is.EqualTo(ForumPage));
            }
        }
    }
}
