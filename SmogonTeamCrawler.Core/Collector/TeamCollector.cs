using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    public partial class TeamCollector : ICollector
    {
        public TeamCollector() : this(null)
        {
        }

        private readonly IDistributedCache? _cache;
        public TeamCollector(IDistributedCache? cache)
        {
            _cache = cache;
        }

        public async Task<IDictionary<string, ICollection<Team>>> Collect(IDictionary<string, string> tierToLinks, bool prefixUsage)
        {
            var collectedTeams = new ConcurrentDictionary<string, ICollection<Team>>();

            // Limit parallelism to prevent memory exhaustion on Linux
            var parallelOptions = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            };

            await Parallel.ForEachAsync(tierToLinks, parallelOptions, async (kv, _) =>
                await CollectFromForum(collectedTeams, kv.Key, kv.Value, prefixUsage).ConfigureAwait(false)).ConfigureAwait(false);

            return collectedTeams;
        }

        public async Task CollectFromForum(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage)
        {
            var pages = 1;
            for (var pageCount = 1; pageCount <= pages; pageCount++)
            {
                var site = await Common.HttpClient.GetStringAsync(url + "page-" + pageCount).ConfigureAwait(false);
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
                        var identifier = prefixUsage ? prefix : tier;
                        if (!collectedTeams.ContainsKey(identifier))
                        {
                            collectedTeams.Add(identifier, []);
                        }

                        ThreadAnalyzeResult analyzeResult;
                        if (await LastIdIsCurrent(fullUrl))
                        {
                            analyzeResult = JsonConvert.DeserializeObject<ThreadAnalyzeResult>(_cache!.GetString(fullUrl)!)!;
                            foreach (var entry in analyzeResult.CollectedTeams)
                            {
                                collectedTeams[identifier].Add(entry);
                            }
                        }
                        else
                        {
                            analyzeResult = await AnalyzeThread(fullUrl, prefix).ConfigureAwait(false);
                            foreach (var entry in analyzeResult.CollectedTeams)
                            {
                                collectedTeams[identifier].Add(entry);
                            }
                            _cache?.SetString(fullUrl, JsonConvert.SerializeObject(analyzeResult));
                        }
                        Console.WriteLine("Added " + analyzeResult.CollectedTeams.Count + " Teams");
                        Console.WriteLine();
                        prefix = "";
                    }
                }
            }
        }

        private async Task<bool> LastIdIsCurrent(string fullUrl)
        {
            if (_cache == null)
            {
                return false;
            }

            string? cachedResults = null;
            try
            {
                cachedResults = _cache.GetString(fullUrl);
            }
            catch (NullReferenceException)
            {
            }
            if (cachedResults == null)
            {
                return false;
            }

            var analyzeResult = JsonConvert.DeserializeObject<ThreadAnalyzeResult>(cachedResults);
            if (analyzeResult == null)
            {
                return false;
            }

            // If the last post is older than two years, assumption is that no new activity with relevant teams will be posted
            if (analyzeResult.LastPost < DateTime.Now.AddYears(-2))
            {
                return true;
            }

            var site = await Common.HttpClient.GetStringAsync(fullUrl + "page-" + analyzeResult.NumberOfPages).ConfigureAwait(false);
            var numberOfPages = GetNumberOfPages(site);
            if (numberOfPages != analyzeResult.NumberOfPages)
            {
                return false;
            }

            var lineDataHandler = new LineDataHandler();
            foreach (var line in site.Split('\n'))
            {
                if (line.Contains("<header class=\"message-attribution message-attribution--split\">"))
                {
                    lineDataHandler.TimerHeader = true;
                }
                else if (line.Contains("data-date-string=\"") && lineDataHandler.TimerHeader)
                {
                    var temp = line[(line.IndexOf("data-date-string=\"") + "data-date-string=\"".Length)..];
                    temp = temp[(temp.IndexOf("title") + "title".Length)..];
                    temp = temp[(temp.IndexOf("\"") + 1)..];
                    temp = temp[..temp.IndexOf("\"")];
                    temp = temp.Replace("at ", "");
                    lineDataHandler.PostDate = DateTime.ParseExact(temp, "MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"));
                    lineDataHandler.TimerHeader = false;
                }
            }

            return lineDataHandler.PostDate == analyzeResult.LastPost;
        }

        public async Task<ThreadAnalyzeResult> AnalyzeThread(string url, string? prefix)
        {
            var pages = 1;
            var latestPost = DateTime.UnixEpoch;
            var collectedTeams = new List<Team>();
            try
            {
                for (var pageCount = 1; pageCount <= pages; pageCount++)
                {
                    var site = await Common.HttpClient.GetStringAsync(url + "page-" + pageCount).ConfigureAwait(false);
                    if (pages == 1)
                    {
                        pages = GetNumberOfPages(site);
                    }

                    var lineDataHandler = new LineDataHandler();

                    var currentTeams = new List<string>();

                    foreach (var line in site.Split('\n'))
                    {
                        await HandleLine(url, collectedTeams, pageCount, currentTeams, line, prefix, lineDataHandler).ConfigureAwait(false);
                    }
                    latestPost = lineDataHandler.PostDate;
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("HttpRequestException at: " + url);
                Console.WriteLine(e.Message);
            }

            return new ThreadAnalyzeResult()
            {
                CollectedTeams = collectedTeams,
                LastPost = latestPost,
                NumberOfPages = pages,
            };
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

        private async Task HandleLine(string url, ICollection<Team> collectedTeams, int pageCount, ICollection<string> currentTeams, string line, string? prefix, LineDataHandler lineDataHandler)
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
                foreach (var team in currentTeams)
                {
                    var tmpTeam = team;
                    var moreTeams = false;
                    while (tmpTeam.Contains("==="))
                    {
                        moreTeams = true;
                        var eqIndex = tmpTeam.IndexOf("===");
                        var teamLine = tmpTeam[(eqIndex + 3)..].TrimStart();
                        if (!teamLine.Contains('\n'))
                        {
                            break;
                        }
                        var newlineIndex = teamLine.IndexOf("\n");
                        teamLine = teamLine[..newlineIndex];

                        string? teamTier = null;
                        if (teamLine.Contains('[') && teamLine.Contains(']'))
                        {
                            var bracketStart = teamLine.IndexOf("[") + 1;
                            var bracketEnd = teamLine.IndexOf("]");
                            teamTier = teamLine[bracketStart..bracketEnd];
                            teamLine = teamLine[(bracketEnd + 1)..];
                        }

                        if (!teamLine.Contains("==="))
                        {
                            break;
                        }

                        var teamTitle = teamLine[..teamLine.IndexOf("===")].Trim();

                        var fullTempTeam = tmpTeam[(eqIndex + 3)..];
                        var fullNewlineIndex = fullTempTeam.IndexOf("\n");
                        if (fullNewlineIndex != -1)
                        {
                            fullTempTeam = fullTempTeam[(fullNewlineIndex + 1)..];
                        }

                        var endIndex = fullTempTeam.IndexOf("===");
                        if (endIndex != -1)
                        {
                            fullTempTeam = fullTempTeam[..endIndex];
                        }

                        var teamObject = new Team(fullTempTeam, lineDataHandler.PostLikes, lineDataHandler.PostDate, url + "page-" + pageCount + "#" + lineDataHandler.PostLink, lineDataHandler.PostedBy, prefix)
                        {
                            TeamTier = teamTier,
                            TeamTitle = teamTitle
                        };
                        collectedTeams.Add(teamObject);

                        tmpTeam = tmpTeam[(eqIndex + 3)..];
                        var nextNewline = tmpTeam.IndexOf("\n");
                        if (nextNewline != -1)
                        {
                            tmpTeam = tmpTeam[(nextNewline + 1)..];
                        }

                        var nextEq = tmpTeam.IndexOf("===");
                        if (nextEq != -1)
                        {
                            tmpTeam = tmpTeam[nextEq..];
                        }
                    }
                    if (!moreTeams)
                    {
                        var teamObject = new Team(team, lineDataHandler.PostLikes, lineDataHandler.PostDate, url + "page-" + pageCount + "#" + lineDataHandler.PostLink, lineDataHandler.PostedBy, prefix);
                        collectedTeams.Add(teamObject);
                    }
                }
                currentTeams.Clear();
                lineDataHandler.PostLikes = 0;
                lineDataHandler.BlockText = "";
                lineDataHandler.PostedBy = "";
            }
            else if (line.Contains("<header class=\"message-attribution message-attribution--split\">"))
            {
                lineDataHandler.TimerHeader = true;
            }
            else if (line.Contains("data-date=\"") && lineDataHandler.TimerHeader)
            {
                var temp = line[(line.IndexOf("data-date-string=\"") + "data-date-string=\"".Length)..];
                temp = temp[(temp.IndexOf("title") + "title".Length)..];
                temp = temp[(temp.IndexOf("\"") + 1)..];
                temp = temp[..temp.IndexOf("\"")];
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
                var likes = CountOccurences(line, "</bdi>");
                if (line.Contains(" others</a>"))
                {
                    var othersIndex = line.IndexOf(" others</a>");
                    var searchStart = Math.Max(0, othersIndex - 50);
                    var tempLikeString = line.Substring(searchStart, Math.Min(50, othersIndex - searchStart));
                    var lastBdiIndex = tempLikeString.LastIndexOf("</bdi>");
                    if (lastBdiIndex > -1)
                    {
                        tempLikeString = tempLikeString[(lastBdiIndex + 6)..].Trim();
                        var andIndex = tempLikeString.IndexOf("and");
                        if (andIndex > -1)
                        {
                            tempLikeString = tempLikeString[(andIndex + 3)..].Trim();
                            var spaceIndex = tempLikeString.IndexOf(" ");
                            if (spaceIndex > -1)
                            {
                                tempLikeString = tempLikeString[..spaceIndex];
                            }
                            if (int.TryParse(tempLikeString, out int parsedLikes))
                            {
                                likes += parsedLikes;
                            }
                        }
                    }
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
                var blockStart = line.IndexOf("<div class=\"bbCodeBlock-content\">") + "<div class=\"bbCodeBlock-content\">".Length;
                var temp = line[blockStart..].Trim();
                if (temp != "<br />" && temp != "")
                {
                    if (lineDataHandler.BlockTextBuilder == null)
                        lineDataHandler.BlockTextBuilder = new StringBuilder();
                    lineDataHandler.BlockTextBuilder.Append(temp).Append('\n');
                }
            }
            else if (lineDataHandler.BlockStarted && ((line.Trim().Replace("\t", "").Contains("</div>") && lineDataHandler.LastLine.Trim().Replace("\t", "").Contains("</div>")) || line.Contains("</div></div>")))
            {
                lineDataHandler.BlockStarted = false;
                var blockTextStr = lineDataHandler.BlockTextBuilder?.ToString() ?? "";
                blockTextStr = blockTextStr.Replace("\t", "").Replace("<br />", "").Replace("</div>", "");
                if (IsTeam(blockTextStr))
                {
                    currentTeams.Add(blockTextStr);
                }
                lineDataHandler.BlockTextBuilder?.Clear();
            }
            else if (lineDataHandler.BlockStarted && (!line.Trim().StartsWith("<") || line.StartsWith("<br />")) && !line.Contains(" -- "))
            {
                if (lineDataHandler.BlockTextBuilder == null)
                    lineDataHandler.BlockTextBuilder = new StringBuilder();
                lineDataHandler.BlockTextBuilder.Append(line).Append('\n');
            }
            if (lineDataHandler.PostStarted)
            {
                if (line.Contains("pokepast.es/"))
                {
                    var pasteUrl = ExtractUrl(line, "pokepast.es/");
                    pasteUrl = "https://" + pasteUrl;
                    if (!pasteUrl.Contains("/img/"))
                    {
                        var pokePasteTeam = await GetTeamFromPokepasteURL(pasteUrl).ConfigureAwait(false);
                        if (IsTeam(pokePasteTeam))
                        {
                            currentTeams.Add(pokePasteTeam);
                        }
                    }
                }
                if (line.Contains("pastebin.com/"))
                {
                    var pasteUrl = ExtractUrl(line, "pastebin.com/");
                    var pasteString = await GetTeamFromBinner(pasteUrl, "pastebin.com").ConfigureAwait(false);
                    if (pasteString != null)
                    {
                        currentTeams.Add(pasteString);
                    }
                }
                if (line.Contains("hastebin.com/"))
                {
                    var pasteUrl = ExtractUrl(line, "hastebin.com/");
                    var pasteString = await GetTeamFromBinner(pasteUrl, "hastebin.com").ConfigureAwait(false);
                    if (pasteString != null)
                    {
                        currentTeams.Add(pasteString);
                    }
                }
            }
            lineDataHandler.LastLine = line;
        }

        private static string ExtractUrl(string line, string urlPart)
        {
            var startIndex = line.IndexOf(urlPart);
            if (startIndex == -1) return "";
            
            var pasteUrl = line[startIndex..];
            if (pasteUrl.Contains(' ') || pasteUrl.Contains('"') || pasteUrl.Contains('<'))
            {
                var nearest = int.MaxValue;
                var space = pasteUrl.IndexOf(" ");
                var quotation = pasteUrl.IndexOf("\"");
                var arrow = pasteUrl.IndexOf("<");

                foreach (var pos in new[] { space, quotation, arrow })
                {
                    if (pos != -1 && pos < nearest)
                    {
                        nearest = pos;
                    }
                }

                if (nearest != int.MaxValue)
                {
                    pasteUrl = pasteUrl[..nearest];
                }
            }
            return pasteUrl;
        }

        private async Task<string?> GetTeamFromBinner(string pasteUrl, string urlRoot)
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

                pasteUrl = pasteUrl[..nearest];
            }
            if (pasteUrl.Contains("/raw/"))
            {
                pasteUrl = "https://" + pasteUrl;
            }
            else
            {
                pasteUrl = "https://" + urlRoot + "/raw/" + pasteUrl[(pasteUrl.IndexOf("/") + 1)..];
            }
            var pasteString = await GetTeamFromBinURL(pasteUrl).ConfigureAwait(false);
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

        [GeneratedRegex("<.*?>")]
        private static partial Regex HtmlRemoverGenerated();
        private readonly Regex htmlRemoverRegex = HtmlRemoverGenerated();

        private async Task<string> GetTeamFromPokepasteURL(string pasteUrl)
        {
            var site = "";
            try
            {
                site = await Common.HttpClient.GetStringAsync(pasteUrl).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            { }

            var team = new StringBuilder();
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
                    team.Append(line).Append('\n');
                }
            }

            return htmlRemoverRegex.Replace(team.ToString(), string.Empty);
        }

        private async Task<string> GetTeamFromBinURL(string pasteUrl)
        {
            var site = "";
            try
            {
                site = await Common.HttpClient.GetStringAsync(pasteUrl).ConfigureAwait(false);
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

        private static int CountOccurences(string haystack, string needle)
        {
            return (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
        }
    }
}
