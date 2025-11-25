using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public class TeamScanner : IScanner
    {
        private const string LINE_CLASS_IDENTIFIER = "class=\"subNodeLink subNodeLink--forum";
        private const string SCAN_ENDPOINT = "node-stats\"";

        public async Task<ScanResult> Scan()
        {
            var scanResult = new ScanResult()
            {
                TierToRegularLinks = new Dictionary<string, string>(),
                TierToRmtLinks = new Dictionary<string, string>(),
            };

            var smogonMain = await Common.HttpClient.GetStringAsync(Common.SMOGON_FORUMS_URL).ConfigureAwait(false);
            var scanStartZero = false;
            var scanStartOne = false;
            var scanStartTwo = false;

            var lines = smogonMain.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var nextLine = i +1 < lines.Length ? lines[i + 1] : "";
                if (scanStartZero)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, nextLine, scanResult.TierToRegularLinks, true);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartZero = false;
                    }
                }
                else if (scanStartOne)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, nextLine, scanResult.TierToRegularLinks, false, true);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartOne = false;
                    }
                }
                else if (scanStartTwo)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, nextLine, scanResult.TierToRmtLinks);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartTwo = false;
                        break;
                    }
                }
                else if (line.Contains("Smogon Metagames"))
                {
                    scanStartZero = true;
                }
                else if (line.Contains("Gen 8 Competitive Discussion"))
                {
                    scanStartOne = true;
                }
                else if (line.Contains("Ruins of Alph"))
                {
                    scanStartOne = true;
                }
                else if (line.Contains(">Rate My Team<"))
                {
                    scanStartTwo = true;
                }
            }

            smogonMain = await Common.HttpClient.GetStringAsync(Common.ARCHIVE_URL).ConfigureAwait(false);
            scanStartTwo = false;
            lines = smogonMain.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var nextLine = i + 1 < lines.Length ? lines[i + 1] : "";
                if (scanStartTwo)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, nextLine, scanResult.TierToRmtLinks);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartTwo = false;
                        break;
                    }
                }
                else if (line.Contains("2020 - 2023 B101 Resources"))
                {
                    scanStartTwo = true;
                }
            }

            return scanResult;
        }

        private static void GetAndAddURL(string line, string nextLine, IDictionary<string, string> addition, bool useNewGen = false, bool useCurrentGen = false)
        {
            var urlName = nextLine[(nextLine.LastIndexOf(">") + 1)..];
            if (useNewGen)
            {
                urlName = Common.NewestGen + urlName;
            }
            if (useCurrentGen)
            {
                urlName = Common.CurrentGen + urlName;
            }

            var url = line[(line.IndexOf('"') + 1)..];
            url = url[..url.IndexOf('"')];
            url = "https://www.smogon.com" + url;

            addition.TryAdd(urlName, url);
        }
    }
}
