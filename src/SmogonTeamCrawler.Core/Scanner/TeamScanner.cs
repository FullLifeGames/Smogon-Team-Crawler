using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public class TeamScanner : IScanner
    {
        private const string LINE_CLASS_IDENTIFIER = "class=\"subNodeLink subNodeLink--forum";
        private const string SCAN_ENDPOINT = "node-stats\"";

        public async Task Scan(IDictionary<string, string> tierToLinks, IDictionary<string, string> tierToRMTLinks)
        {
            var smogonMain = await Common.HttpClient.GetStringAsync(Common.SMOGON_FORUMS_URL);
            var scanStartZero = false;
            var scanStartOne = false;
            var scanStartTwo = false;

            foreach (var line in smogonMain.Split('\n'))
            {
                if (scanStartZero)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, tierToLinks, true);
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
                        GetAndAddURL(line, tierToLinks);
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
                        GetAndAddURL(line, tierToRMTLinks);
                    }
                    else if (line.Contains(SCAN_ENDPOINT))
                    {
                        scanStartTwo = false;
                        break;
                    }
                }
                else if (line.Contains("Uncharted Territory"))
                {
                    scanStartZero = true;
                }
                else if (line.Contains("Smogon Metagames"))
                {
                    scanStartOne = true;
                }
                else if (line.Contains(">Rate My Team<"))
                {
                    scanStartTwo = true;
                }
            }

            smogonMain = await Common.HttpClient.GetStringAsync(Common.ARCHIVE_URL);
            scanStartTwo = false;
            foreach (var line in smogonMain.Split('\n'))
            {
                if (scanStartTwo)
                {
                    if (line.Contains(LINE_CLASS_IDENTIFIER))
                    {
                        GetAndAddURL(line, tierToRMTLinks);
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
        }

        private void GetAndAddURL(string line, IDictionary<string, string> addition, bool useCurrentGen = false)
        {
            var urlName = line[(line.IndexOf(">") + 1)..];
            urlName = urlName[..urlName.IndexOf("<")];
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
