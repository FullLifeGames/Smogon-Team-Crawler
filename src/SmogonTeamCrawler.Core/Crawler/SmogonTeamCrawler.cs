using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Formatter;
using SmogonTeamCrawler.Core.Scanner;
using SmogonTeamCrawler.Core.Transformer;

namespace SmogonTeamCrawler.Core.Crawler
{
    public class SmogonTeamCrawler : TeamCrawler
    {
        public override ICollector Collector => new TeamCollector();

        public override IFormatter Formatter => new TeamFormatter();

        public override IScanner Scanner => new TeamScanner();

        public override ITransformer Transformer => new TeamTransformer();
    }
}
