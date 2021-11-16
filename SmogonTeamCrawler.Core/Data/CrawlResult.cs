using System;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    public class CrawlResult
    {
        public IDictionary<string, ICollection<Team>> SmogonTeams { get; set; }
        public IDictionary<string, ICollection<Team>> Rmts { get; set; }

        public string SmogonOutput { get; set; }
        public string RmtsOutput { get; set; }

        public IDictionary<string, string> TeamsByTier { get; set; }
    }
}
