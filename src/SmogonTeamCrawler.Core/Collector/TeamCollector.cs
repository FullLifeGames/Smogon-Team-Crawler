﻿using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    public class TeamCollector : ICollector
    {
        public async Task<IDictionary<string, ICollection<Team>>> Collect(IDictionary<string, string> tierToLinks, bool prefixUsage)
        {
            var teamsForTiers = new ConcurrentDictionary<string, ICollection<Team>>();

            await Parallel.ForEachAsync(tierToLinks, async (kv, ct) =>
            {
                await CollectFromForum(teamsForTiers, kv.Key, kv.Value, prefixUsage);
            });

            return teamsForTiers;
        }

        public async Task CollectFromForum(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage)
        {
            var pages = 1;
            for (var pageCount = 1; pageCount <= pages; pageCount++)
            {
                var site = await Common.HttpClient.GetStringAsync(url + "page-" + pageCount);
                if (pages == 1)
                {
                    pages = GetNumberOfPages(site);
                }

                var prefix = "";

                foreach (var line in site.Split('\n'))
                {
                    if (line.Contains("<span class=\"label"))
                    {
                        prefix = line[(line.IndexOf("<span class=\"label") + 1)..];
                        prefix = prefix[(prefix.IndexOf(">") + 1)..];
                        prefix = prefix[..prefix.IndexOf("<")];
                    }
                    else if (line.Contains("data-preview-url"))
                    {
                        var tempInside = line[(line.IndexOf("data-preview-url") + "data-preview-url".Length)..];
                        tempInside = tempInside[(tempInside.IndexOf("\"") + 1)..];
                        if (!tempInside.Contains("/preview"))
                        {
                            continue;
                        }
                        tempInside = tempInside[..(tempInside.IndexOf("/preview") + 1)];
                        var fullUrl = "https://www.smogon.com" + tempInside;
                        Console.WriteLine("Currently Scanning: " + fullUrl);
                        var identifier = (prefixUsage) ? prefix : tier;
                        if (!collectedTeams.ContainsKey(identifier))
                        {
                            collectedTeams.Add(identifier, new List<Team>());
                        }
                        int beforeCount = collectedTeams[identifier].Count;
                        await AnalyzeThread(collectedTeams, tier: identifier, fullUrl, prefix);
                        int afterCount = collectedTeams[identifier].Count;
                        Console.WriteLine("Added " + (afterCount - beforeCount) + " Teams");
                        Console.WriteLine();
                        prefix = "";
                    }
                }
            }
        }

        public async Task AnalyzeThread(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, string prefix)
        {
            try
            {
                var pages = 1;
                for (var pageCount = 1; pageCount <= pages; pageCount++)
                {
                    var site = await Common.HttpClient.GetStringAsync(url + "page-" + pageCount);
                    if (pages == 1)
                    {
                        pages = GetNumberOfPages(site);
                    }

                    var lineDataHandler = new LineDataHandler()
                    {
                        BlockStarted = false,
                        BlockText = "",

                        PostStarted = false,
                        PostLink = "",
                        PostLikes = 0,
                        PostDate = DateTime.Now,

                        PostedBy = "",

                        LikeStarted = false,

                        TimerHeader = false,

                        LastLine = "",
                    };

                    var currentTeams = new List<string>();

                    foreach (var line in site.Split('\n'))
                    {
                        await HandleLine(url, tier, collectedTeams, pageCount, currentTeams, line, prefix, lineDataHandler);
                    }
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("HttpRequestException bei: " + url);
                Console.WriteLine(e.Message);
            }
        }

        private static int GetNumberOfPages(string site)
        {
            var pages = 1;
            if (site.Contains("<nav class=\"pageNavWrapper"))
            {
                var temp = site;
                while (temp.Contains("pageNav-page"))
                {
                    temp = temp[(temp.IndexOf("pageNav-page") + "pageNav-page".Length)..];
                }
                temp = temp[(temp.IndexOf(">") + 1)..];
                temp = temp[(temp.IndexOf(">") + 1)..];
                temp = temp[..temp.IndexOf("<")];
                pages = int.Parse(temp);
            }

            return pages;
        }

        public class LineDataHandler
        {
            public bool BlockStarted { get; set; }
            public string BlockText { get; set; }
            public bool PostStarted { get; set; }
            public int PostLikes { get; set; }
            public DateTime PostDate { get; set; }
            public string PostedBy { get; set; }
            public string LastLine { get; set; }
            public string PostLink { get; set; }
            public bool LikeStarted { get; set; }
            public bool TimerHeader { get; set; }
        }

        private async Task HandleLine(string url, string tier, IDictionary<string, ICollection<Team>> collectedTeams, int pageCount, ICollection<string> currentTeams, string line, string prefix, LineDataHandler lineDataHandler)
        {
            if (!lineDataHandler.PostStarted)
            {
                if (line.Contains("<article class=\"message message--post js-post js-inlineModContainer"))
                {
                    lineDataHandler.PostStarted = true;
                }
            }
            else if (line.Contains("data-author=\""))
            {
                lineDataHandler.PostedBy = line[(line.IndexOf("data-author") + 5)..];
                lineDataHandler.PostedBy = lineDataHandler.PostedBy[(lineDataHandler.PostedBy.IndexOf("\"") + 1)..];
                lineDataHandler.PostedBy = lineDataHandler.PostedBy[..lineDataHandler.PostedBy.IndexOf("\"")];
            }
            else if (line.Contains("data-content=\""))
            {
                lineDataHandler.PostLink = line[(line.IndexOf("data-content=\"") + 5)..];
                lineDataHandler.PostLink = lineDataHandler.PostLink[(lineDataHandler.PostLink.IndexOf("\"") + 1)..];
                lineDataHandler.PostLink = lineDataHandler.PostLink[..lineDataHandler.PostLink.IndexOf("\"")];
            }
            else if (line.StartsWith("\t</article>"))
            {
                lineDataHandler.PostStarted = false;
                foreach (string team in currentTeams)
                {
                    string tmpTeam = team;
                    bool moreTeams = false;
                    while (tmpTeam.Contains("==="))
                    {
                        moreTeams = true;
                        string teamLine = tmpTeam[(tmpTeam.IndexOf("===") + "===".Length)..];
                        if (!teamLine.Contains('\n'))
                        {
                            break;
                        }
                        teamLine = teamLine[..teamLine.IndexOf("\n")];

                        string teamTier = null;
                        if (teamLine.Contains('[') && teamLine.Contains(']'))
                        {
                            teamTier = teamLine.Substring(teamLine.IndexOf("[") + 1, teamLine.IndexOf("]") - teamLine.IndexOf("[") - 1);
                            teamLine = teamLine[(teamLine.IndexOf("]") + 1)..];
                        }

                        if (!teamLine.Contains("==="))
                        {
                            break;
                        }

                        string teamTitle = teamLine[..teamLine.IndexOf("===")].Trim();

                        string fullTempTeam = tmpTeam[(tmpTeam.IndexOf("===") + "===".Length)..];
                        fullTempTeam = fullTempTeam[(fullTempTeam.IndexOf("\n") + 1)..];

                        if (fullTempTeam.Contains("==="))
                        {
                            fullTempTeam = fullTempTeam[..fullTempTeam.IndexOf("===")];
                        }

                        var teamObject = new Team(fullTempTeam, lineDataHandler.PostLikes, lineDataHandler.PostDate, url + "page-" + pageCount + "#" + lineDataHandler.PostLink, lineDataHandler.PostedBy, prefix)
                        {
                            TeamTier = teamTier,
                            TeamTitle = teamTitle
                        };
                        collectedTeams[tier].Add(teamObject);

                        tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("===") + "===".Length);
                        tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("\n") + 1);

                        if (tmpTeam.Contains("==="))
                        {
                            tmpTeam = tmpTeam.Substring(tmpTeam.IndexOf("==="));
                        }
                    }
                    if (!moreTeams)
                    {
                        var teamObject = new Team(team, lineDataHandler.PostLikes, lineDataHandler.PostDate, url + "page-" + pageCount + "#" + lineDataHandler.PostLink, lineDataHandler.PostedBy, prefix);
                        collectedTeams[tier].Add(teamObject);
                    }
                }
                currentTeams.Clear();
                lineDataHandler.PostLikes = 0;
                lineDataHandler.BlockText = "";
                lineDataHandler.PostedBy = "";
                lineDataHandler.PostDate = DateTime.Now;
            }
            else if (line.Contains("<header class=\"message-attribution message-attribution--split\">"))
            {
                lineDataHandler.TimerHeader = true;
            }
            else if (line.Contains("data-date-string=\"") && lineDataHandler.TimerHeader)
            {
                string temp = line.Substring(line.IndexOf("data-date-string=\"") + "data-date-string=\"".Length);
                temp = temp.Substring(temp.IndexOf("title") + "title".Length);
                temp = temp.Substring(temp.IndexOf("\"") + 1);
                temp = temp.Substring(0, temp.IndexOf("\""));
                temp = temp.Replace("at ", "");
                lineDataHandler.PostDate = DateTime.ParseExact(temp, "MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                lineDataHandler.TimerHeader = false;
            }
            else if (line.Contains("<span class=\"u-srOnly\">Reactions:</span>"))
            {
                lineDataHandler.LikeStarted = true;
            }
            else if (lineDataHandler.LikeStarted)
            {
                lineDataHandler.LikeStarted = false;
                int likes = CountOccurences(line, "</bdi>");
                if (line.Contains(" others</a>"))
                {
                    string tempLikeString = line;
                    while (tempLikeString.Contains("</bdi>"))
                    {
                        tempLikeString = tempLikeString.Substring(tempLikeString.IndexOf("</bdi>") + "</bdi>".Length);
                    }
                    tempLikeString = tempLikeString.Substring(tempLikeString.IndexOf("and") + "and".Length).Trim();
                    tempLikeString = tempLikeString.Substring(0, tempLikeString.IndexOf(" "));
                    likes += int.Parse(tempLikeString);
                }
                else if (line.Contains("1 other person</a>"))
                {
                    likes++;
                }
                lineDataHandler.PostLikes = likes;
            }
            else if (line.Contains("<div class=\"bbCodeBlock-content\">"))
            {
                lineDataHandler.BlockStarted = true;
                lineDataHandler.BlockText = "";
                string temp = line.Substring(line.IndexOf("<div class=\"bbCodeBlock-content\">") + "<div class=\"bbCodeBlock-content\">".Length).Trim();
                if (temp != "<br />" && temp != "")
                {
                    lineDataHandler.BlockText += temp + "\n";
                }
            }
            else if (lineDataHandler.BlockStarted && ((line.Trim().Replace("\t", "").Contains("</div>") && lineDataHandler.LastLine.Trim().Replace("\t", "").Contains("</div>")) || line.Contains("</div></div>")))
            {
                lineDataHandler.BlockStarted = false;
                lineDataHandler.BlockText = lineDataHandler.BlockText.Replace("\t", "");
                lineDataHandler.BlockText = lineDataHandler.BlockText.Replace("<br />", "");
                lineDataHandler.BlockText = lineDataHandler.BlockText.Replace("</div>", "");
                if (IsTeam(lineDataHandler.BlockText))
                {
                    currentTeams.Add(lineDataHandler.BlockText);
                }
            }
            else if (lineDataHandler.BlockStarted && (!line.Trim().StartsWith("<") || line.StartsWith("<br />")) && !line.Contains(" -- "))
            {
                lineDataHandler.BlockText += line + "\n";
            }
            if (lineDataHandler.PostStarted)
            {
                if (line.Contains("pokepast.es/"))
                {
                    string pasteUrl = line.Substring(line.IndexOf("pokepast.es/"));
                    if (pasteUrl.Contains(' ') || pasteUrl.Contains('"') || pasteUrl.Contains('<'))
                    {
                        int nearest = int.MaxValue;
                        int space = pasteUrl.IndexOf(" ");
                        int quotation = pasteUrl.IndexOf("\"");
                        int arrow = pasteUrl.IndexOf("<");

                        foreach (int pos in new int[] { space, quotation, arrow })
                        {
                            if (pos != -1 && pos < nearest)
                            {
                                nearest = pos;
                            }
                        }

                        pasteUrl = pasteUrl[..nearest];
                    }
                    pasteUrl = "https://" + pasteUrl;
                    if (!pasteUrl.Contains("/img/"))
                    {
                        string pokePasteTeam = await GetTeamFromPokepasteURL(pasteUrl);
                        if (IsTeam(pokePasteTeam))
                        {
                            currentTeams.Add(pokePasteTeam);
                        }
                    }
                }
                if (line.Contains("pastebin.com/"))
                {
                    string pasteUrl = line[line.IndexOf("pastebin.com/")..];
                    var pasteString = await GetTeamFromBinner(pasteUrl, "pastebin.com");
                    if (pasteString != null)
                    {
                        currentTeams.Add(pasteString);
                    }
                }
                if (line.Contains("hastebin.com/"))
                {
                    string pasteUrl = line[line.IndexOf("hastebin.com/")..];
                    var pasteString = await GetTeamFromBinner(pasteUrl, "hastebin.com");
                    if (pasteString != null)
                    {
                        currentTeams.Add(pasteString);
                    }
                }
            }
            lineDataHandler.LastLine = line;
        }

        private async Task<string> GetTeamFromBinner(string pasteUrl, string urlRoot)
        {
            if (pasteUrl.Contains(' ') || pasteUrl.Contains('"') || pasteUrl.Contains('<'))
            {
                var nearest = int.MaxValue;
                var space = pasteUrl.IndexOf(" ");
                var quotation = pasteUrl.IndexOf("\"");
                var arrow = pasteUrl.IndexOf("<");

                foreach (var pos in new int[] { space, quotation, arrow })
                {
                    if (pos != -1 && pos < nearest)
                    {
                        nearest = pos;
                    }
                }

                pasteUrl = pasteUrl.Substring(0, nearest);
            }
            if (pasteUrl.Contains("/raw/"))
            {
                pasteUrl = "https://" + pasteUrl;
            }
            else
            {
                pasteUrl = "https://" + urlRoot + "/raw/" + pasteUrl.Substring(pasteUrl.IndexOf("/") + 1);
            }
            var pasteString = await GetTeamFromBinURL(pasteUrl);
            if (IsTeam(pasteString))
            {
                var blackListed = false;
                foreach (var urlPart in Common.HardCodedBlacklistedPastes)
                {
                    if (pasteUrl.Contains(urlPart))
                    {
                        blackListed = true;
                    }
                }
                if (!blackListed)
                {
                    return pasteString;
                }
            }
            return null;
        }

        private readonly Regex htmlRemoverRegex = new("<.*?>");

        private async Task<string> GetTeamFromPokepasteURL(string pasteUrl)
        {
            var site = "";
            try
            {
                site = await Common.HttpClient.GetStringAsync(pasteUrl);
            }
            catch (HttpRequestException)
            { }

            var team = "";

            var addLines = false;

            foreach (var line in site.Split('\n'))
            {
                if (line.Contains("<pre>"))
                {
                    addLines = true;
                }
                else if (line.Contains("</pre>"))
                {
                    addLines = false;
                }
                if (addLines)
                {
                    team += line + "\n";
                }
            }

            team = htmlRemoverRegex.Replace(team, string.Empty);

            return team;
        }

        private async Task<string> GetTeamFromBinURL(string pasteUrl)
        {
            var site = "";
            try
            {
                site = await Common.HttpClient.GetStringAsync(pasteUrl);
            }
            catch (HttpRequestException)
            { }

            var team = htmlRemoverRegex.Replace(site, string.Empty);

            return team;
        }

        private readonly Regex doubleRowRegex = new(@"\n\n");

        private bool IsTeam(string blockText)
        {
            var split = doubleRowRegex.Split(blockText.Replace("\r", ""));
            var countFullMoves = 0;
            foreach (var mon in split)
            {
                if (CountOccurences(mon, "\n- ") >= 4)
                {
                    countFullMoves++;
                }
            }
            return countFullMoves >= 6 || (CountOccurences(blockText, "EVs: ") >= 6 && CountOccurences(blockText, "Nature") >= 6 && CountOccurences(blockText, "Ability: ") >= 6);
        }

        private int CountOccurences(string haystack, string needle)
        {
            return (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
        }
    }
}