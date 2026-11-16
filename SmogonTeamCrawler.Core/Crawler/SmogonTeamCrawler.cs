using Microsoft.Extensions.Caching.Distributed;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Formatter;
using SmogonTeamCrawler.Core.Scanner;
using SmogonTeamCrawler.Core.Transformer;

namespace SmogonTeamCrawler.Core.Crawler
{
    public class SmogonTeamCrawler : TeamCrawler
    {
        public SmogonTeamCrawler() : this(null) { }

        /// <param name="cache">
        /// Persistent cache for the incremental state. Without one, every run reads everything again.
        /// </param>
        public SmogonTeamCrawler(IDistributedCache? cache) : this(cache, null) { }

        public SmogonTeamCrawler(IDistributedCache? cache, CollectorOptions? options)
        {
            _collector = new TeamCollector(cache, options);
            _scanner = new TeamScanner(options?.HttpClient);
        }

        private readonly ICollector _collector;
        private readonly IScanner _scanner;

        public override ICollector Collector => _collector;

        public override IFormatter Formatter => new TeamFormatter();

        public override IScanner Scanner => _scanner;

        public override ITransformer Transformer => new TeamTransformer();
    }
}
