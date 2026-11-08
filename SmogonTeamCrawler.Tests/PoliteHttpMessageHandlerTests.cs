#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class PoliteHttpMessageHandlerTests
    {
        /// <summary>Answers with the queued status codes in order and records when each request arrived.</summary>
        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses = new();
            private readonly Stopwatch _clock = Stopwatch.StartNew();

            public List<(TimeSpan At, HttpRequestMessage Request)> Calls { get; } = new();

            public ScriptedHandler Then(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
            {
                _responses.Enqueue(() =>
                {
                    var response = new HttpResponseMessage(statusCode);
                    if (retryAfter != null)
                    {
                        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
                    }
                    return response;
                });
                return this;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Calls)
                {
                    Calls.Add((_clock.Elapsed, request));
                }
                var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
                return Task.FromResult(next());
            }
        }

        private static readonly PoliteHttpOptions FastOptions = new()
        {
            MinDelay = TimeSpan.Zero,
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromSeconds(5),
            MaxRetries = 2,
        };

        private static (HttpClient Client, PoliteHttpMessageHandler Handler) Create(ScriptedHandler inner, PoliteHttpOptions options)
        {
            var handler = new PoliteHttpMessageHandler(options, inner);
            return (new HttpClient(handler), handler);
        }

        [Test]
        public async Task RequestsToTheSameHost_AreSpacedByMinDelay()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.OK);
            var (client, _) = Create(inner, new PoliteHttpOptions { MinDelay = TimeSpan.FromMilliseconds(200) });

            await Task.WhenAll(Enumerable.Range(0, 3).Select(i => client.GetAsync("https://www.smogon.com/forums/" + i)));

            var times = inner.Calls.Select(c => c.At).OrderBy(t => t).ToList();
            Assert.That(times[1] - times[0], Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180)));
            Assert.That(times[2] - times[1], Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180)));
        }

        [Test]
        public async Task TooManyRequests_IsRetriedAfterRetryAfter()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(1)).Then(HttpStatusCode.OK);
            var (client, handler) = Create(inner, FastOptions);

            var response = await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.Calls, Has.Count.EqualTo(2));
            Assert.That(inner.Calls[1].At - inner.Calls[0].At, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950)));
            Assert.That(handler.Statistics.For("www.smogon.com"), Is.EqualTo(2));
        }

        [Test]
        public async Task RetryAfterLongerThanMaxBackoff_GivesUpWithoutRetry()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.TooManyRequests, TimeSpan.FromHours(1));
            var (client, _) = Create(inner, FastOptions);

            var response = await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(inner.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task NotFound_IsNotRetried()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.NotFound);
            var (client, _) = Create(inner, FastOptions);

            var response = await client.GetAsync("https://www.smogon.com/forums/threads/gone.1/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(inner.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ServerErrors_AreRetriedUpToMaxRetries()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.ServiceUnavailable);
            var (client, _) = Create(inner, FastOptions);

            var response = await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(inner.Calls, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task ServerError_FollowedBySuccess_ReturnsTheSuccess()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK);
            var (client, _) = Create(inner, FastOptions);

            var response = await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inner.Calls, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task HangingAttempt_TimesOutAndIsRetried()
        {
            var calls = 0;
            var inner = new DelegateHandler(async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var options = new PoliteHttpOptions { MinDelay = TimeSpan.Zero, InitialBackoff = TimeSpan.FromMilliseconds(10), RequestTimeout = TimeSpan.FromMilliseconds(200) };
            var client = PoliteHttpMessageHandler.CreateClient(new PoliteHttpMessageHandler(options, inner));

            var response = await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(calls, Is.EqualTo(2));
        }

        private class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                send(request, cancellationToken);
        }

        [Test]
        public async Task Requests_IdentifyTheCrawler()
        {
            var inner = new ScriptedHandler().Then(HttpStatusCode.OK);
            var (client, _) = Create(inner, FastOptions);

            await client.GetAsync("https://www.smogon.com/forums/");

            Assert.That(inner.Calls[0].Request.Headers.UserAgent.ToString(), Does.StartWith("SmogonTeamCrawler/"));
        }
    }
}
