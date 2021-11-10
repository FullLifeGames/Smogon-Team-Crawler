using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public interface IScanner
    {
        public Task Scan(Dictionary<string, string> tierToLinks, Dictionary<string, string> tierToRMTLinks);
    }
}
