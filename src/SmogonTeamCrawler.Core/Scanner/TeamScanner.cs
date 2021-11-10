using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public class TeamScanner : IScanner
    {

        public async Task Scan(Dictionary<string, string> tierToLinks, Dictionary<string, string> tierToRMTLinks)
        {
            var smogonMain = await Common.HttpClient.GetStringAsync("https://www.smogon.com/forums/");
            var scanStartZero = false;
            var scanStartOne = false;
            var scanStartTwo = false;

            foreach (string line in smogonMain.Split('\n'))
            {
                if (scanStartZero)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string tierName = line.Substring(line.IndexOf(">") + 1);
                        tierName = tierName.Substring(0, tierName.IndexOf("<"));
                        tierName = Common.CurrentGen + tierName;

                        string tierUrl = line.Substring(line.IndexOf("\"") + 1);
                        tierUrl = tierUrl.Substring(0, tierUrl.IndexOf("\""));
                        tierUrl = "https://www.smogon.com" + tierUrl;

                        tierToLinks.Add(tierName, tierUrl);
                    }
                    else if (line.Contains("node-stats\""))
                    {
                        scanStartZero = false;
                    }
                }
                else if (scanStartOne)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string tierName = line.Substring(line.IndexOf(">") + 1);
                        tierName = tierName.Substring(0, tierName.IndexOf("<"));

                        string tierUrl = line.Substring(line.IndexOf("\"") + 1);
                        tierUrl = tierUrl.Substring(0, tierUrl.IndexOf("\""));
                        tierUrl = "https://www.smogon.com" + tierUrl;

                        tierToLinks.Add(tierName, tierUrl);
                    }
                    else if (line.Contains("node-stats\""))
                    {
                        scanStartOne = false;
                    }
                }
                else if (scanStartTwo)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string urlName = line.Substring(line.IndexOf(">") + 1);
                        urlName = urlName.Substring(0, urlName.IndexOf("<"));

                        string url = line.Substring(line.IndexOf("\"") + 1);
                        url = url.Substring(0, url.IndexOf("\""));
                        url = "https://www.smogon.com" + url;

                        tierToRMTLinks.Add(urlName, url);
                    }
                    else if (line.Contains("node-stats\""))
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

            smogonMain = await Common.HttpClient.GetStringAsync("https://www.smogon.com/forums/categories/site-projects-archive.423/");
            scanStartTwo = false;
            foreach (string line in smogonMain.Split('\n'))
            {
                if (scanStartTwo)
                {
                    if (line.Contains("class=\"subNodeLink subNodeLink--forum"))
                    {
                        string urlName = line.Substring(line.IndexOf(">") + 1);
                        urlName = urlName.Substring(0, urlName.IndexOf("<"));

                        string url = line.Substring(line.IndexOf("\"") + 1);
                        url = url.Substring(0, url.IndexOf("\""));
                        url = "https://www.smogon.com" + url;

                        tierToRMTLinks.Add(urlName, url);
                    }
                    else if (line.Contains("node-stats\""))
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
    }
}
