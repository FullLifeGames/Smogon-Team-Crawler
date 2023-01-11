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

            foreach (var line in smogonMain.Split('\n'))
            {
                if (scanStartZero)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, scanResult.TierToRegularLinks, true);
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
                        GetAndAddURL(line, scanResult.TierToRegularLinks, false, true);
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
                        GetAndAddURL(line, scanResult.TierToRmtLinks);
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
            foreach (var line in smogonMain.Split('\n'))
            {
                if (scanStartTwo)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, scanResult.TierToRmtLinks);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartTwo = false;
                        break;
                    }
                }
                else if (line.Contains(">2007 - 2019 Rate My Team<"))
                {
                    scanStartTwo = true;
                }
            }

            return scanResult;
        }

        private static void GetAndAddURL(string line, IDictionary<string, string> addition, bool useNewGen = false, bool useCurrentGen = false)
        {
            var urlName = line[(line.IndexOf(">") + 1)..];
            urlName = urlName[..urlName.IndexOf("<")];
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

            addition.Add(urlName, url);
        }
    }
}
