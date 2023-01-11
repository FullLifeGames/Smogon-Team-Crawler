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

        private readonly IDistributedCache? _cache;
        public SmogonTeamCrawler(IDistributedCache? cache)
        {
            _cache = cache;
        }

        public override ICollector Collector => new TeamCollector(_cache);

        public override IFormatter Formatter => new TeamFormatter();

        public override IScanner Scanner => new TeamScanner();

        public override ITransformer Transformer => new TeamTransformer();
    }
}
