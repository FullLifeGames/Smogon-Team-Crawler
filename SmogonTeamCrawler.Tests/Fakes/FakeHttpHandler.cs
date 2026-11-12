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
        private readonly Dictionary<string, Task> _holds = new();

        public List<string> Requests { get; } = new();

        /// <summary>Called with the URL of every request, before it is answered.</summary>
        public Action<string>? OnRequest { get; set; }

        public void Html(string url, string html) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };

        public void Status(string url, HttpStatusCode statusCode) =>
            _routes[url] = () => new HttpResponseMessage(statusCode);

        /// <summary>Answers requests for <paramref name="url"/> only once <paramref name="release"/> completed.</summary>
        public void Hold(string url, Task release) => _holds[url] = release;

        /// <summary>Answers every URL starting with <paramref name="urlPrefix"/> that has no route of its own.</summary>
        public void Prefix(string urlPrefix, Func<string, HttpResponseMessage> respond) => _prefixes[urlPrefix] = respond;

        private readonly Dictionary<string, Func<string, HttpResponseMessage>> _prefixes = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(url);
            }
            OnRequest?.Invoke(url);
            if (_holds.TryGetValue(url, out var release))
            {
                await release.WaitAsync(cancellationToken);
            }
            if (_routes.TryGetValue(url, out var route))
            {
                return route();
            }
            foreach (var (prefix, respond) in _prefixes)
            {
                if (url.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return respond(url);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
