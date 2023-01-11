using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    public class ScanResult
    {
        public IDictionary<string, string> TierToRegularLinks { get; set; } = null!;
        public IDictionary<string, string> TierToRmtLinks { get; set; } = null!;
    }
}
