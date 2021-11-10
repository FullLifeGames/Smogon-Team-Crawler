using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    public interface ICollector
    {
        public Task<Dictionary<string, List<Team>>> Collect(Dictionary<string, string> tierToLinks, bool prefixUsage);
    }
}
