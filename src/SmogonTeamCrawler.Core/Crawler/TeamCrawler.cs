using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Formatter;
using SmogonTeamCrawler.Core.Scanner;
using SmogonTeamCrawler.Core.Transformer;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Crawler
{
    public abstract class TeamCrawler
    {
        public abstract ICollector Collector
        {
            get;
        }
        public abstract IFormatter Formatter
        {
            get;
        }
        public abstract IScanner Scanner
        {
            get;
        }
        public abstract ITransformer Transformer
        {
            get;
        }

        public CrawlResult Crawl(CrawlRequest crawlRequest)
        {
            return CrawlAsync(crawlRequest).Result;
        }

        public async Task<CrawlResult> CrawlAsync(CrawlRequest crawlRequest)
        {
            IDictionary<string, string> tierToLinks = new Dictionary<string, string>();
            IDictionary<string, string> tierToRMTLinks = new Dictionary<string, string>();
            await Scanner.Scan(tierToLinks, tierToRMTLinks);

            IDictionary<string, ICollection<Team>> teamsForTiers = new Dictionary<string, ICollection<Team>>();
            if (crawlRequest.MainForum)
            {
                teamsForTiers = await Collector.Collect(tierToLinks, false);
            }
            IDictionary<string, ICollection<Team>> rmtForTiers = new Dictionary<string, ICollection<Team>>();
            if (crawlRequest.RMTForum)
            {
                rmtForTiers = await Collector.Collect(tierToRMTLinks, true);
            }

            var output = "";
            if (crawlRequest.MainForum)
            {
                output = Formatter.FormatOutput(teamsForTiers);
            }
            var outputRMT = "";
            if (crawlRequest.RMTForum)
            {
                outputRMT = Formatter.FormatOutput(rmtForTiers);
            }

            var tierOutputs = Transformer.Transform(teamsForTiers, rmtForTiers);

            return new CrawlResult()
            {
                SmogonTeams = teamsForTiers,
                Rmts = rmtForTiers,

                SmogonOutput = output,
                RmtsOutput = outputRMT,

                TeamsByTier = tierOutputs,
            };
        }
    }
}
