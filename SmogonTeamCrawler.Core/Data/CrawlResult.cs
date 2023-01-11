using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    public class CrawlResult
    {
        public IDictionary<string, ICollection<Team>> CreatedTeamsByTiers { get; set; } = null!;
        public IDictionary<string, string> TeamsByTier { get; set; } = null!;
    }
}
