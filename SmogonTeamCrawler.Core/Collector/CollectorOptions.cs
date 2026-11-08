using System;
using System.Net.Http;

namespace SmogonTeamCrawler.Core.Collector
{
    public class CollectorOptions
    {
        /// <summary>Ignore the stored state: walk every listing page and read every thread page again.</summary>
        public bool FullRefresh { get; set; }

        /// <summary>
        /// When a thread has new posts, pages whose newest post is younger than this are read again as well,
        /// so that likes on recent posts stay current. <see cref="TimeSpan.Zero"/> turns this off.
        /// </summary>
        public TimeSpan RecentPageRefreshWindow { get; set; } = TimeSpan.FromDays(7);

        /// <summary>HttpClient for all requests; defaults to <see cref="Util.Common.HttpClient"/>.</summary>
        public HttpClient? HttpClient { get; set; }
    }
}
