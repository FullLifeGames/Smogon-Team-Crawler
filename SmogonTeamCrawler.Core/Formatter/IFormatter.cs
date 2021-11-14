using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Formatter
{
    public interface IFormatter
    {
        public string FormatOutput(IDictionary<string, ICollection<Team>> teamsForTiers);
    }
}
