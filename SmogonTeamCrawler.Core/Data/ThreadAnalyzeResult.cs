using System;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    public class ThreadAnalyzeResult
    {
        public ICollection<Team> CollectedTeams = null!;
        public int NumberOfPages;
        public DateTime LastPost;
    }
}