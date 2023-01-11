using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    public interface ICollector
    {
        public Task<IDictionary<string, ICollection<Team>>> Collect(IDictionary<string, string> tierToLinks, bool prefixUsage);
        public Task CollectFromForum(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage);
        public Task<ThreadAnalyzeResult> AnalyzeThread(string url, string? prefix);
    }
}
