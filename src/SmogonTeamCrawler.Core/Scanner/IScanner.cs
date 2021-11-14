using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public interface IScanner
    {
        public Task Scan(IDictionary<string, string> tierToLinks, IDictionary<string, string> tierToRMTLinks);
    }
}
