using System;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    public class CrawlResult
    {
        public Dictionary<string, List<Team>> SmogonTeams { get; set; }
        public Dictionary<string, List<Team>> Rmts { get; set; }

        public string SmogonOutput { get; set; }
        public string RmtsOutput { get; set; }

        public Dictionary<string, string> TeamsByTier { get; set; }
    }
}
