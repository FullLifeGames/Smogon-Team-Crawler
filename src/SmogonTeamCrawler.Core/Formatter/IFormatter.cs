using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Formatter
{
    public interface IFormatter
    {
        public string FormatOutput(Dictionary<string, List<Team>> teamsForTiers);
    }
}
