using SmogonTeamCrawler.Core.Data;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public interface IScanner
    {
        public Task<ScanResult> Scan();
    }
}
