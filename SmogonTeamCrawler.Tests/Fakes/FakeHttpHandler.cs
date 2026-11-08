#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests.Fakes
{
    /// <summary>Serves registered URLs and records every request; unknown URLs answer 404.</summary>
    internal class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();

        public List<string> Requests { get; } = new();

        public void Html(string url, string html) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };

        public void Status(string url, HttpStatusCode statusCode) =>
            _routes[url] = () => new HttpResponseMessage(statusCode);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(url);
            }
            return Task.FromResult(_routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
