using Microsoft.Extensions.Caching.Distributed;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>
    /// Collects teams from Smogon forums. With a persistent cache, a run only reads what changed since the last run:
    /// listing pages up to the first known, unchanged thread, and of changed threads only the pages from the last known page on
    /// (plus pages with recent posts, see <see cref="CollectorOptions.RecentPageRefreshWindow"/>).
    /// </summary>
    public partial class TeamCollector : ICollector
    {
        private const int MaxUnexpectedThreadPagesInARow = 5;

        private readonly CrawlStateStore _store;
        private readonly CollectorOptions _options;
        private readonly HttpClient _httpClient;
        private int _unexpectedThreadPagesInARow;

        public TeamCollector() : this(null)
        {
        }

        public TeamCollector(IDistributedCache? cache) : this(cache, null)
        {
        }

        public TeamCollector(IDistributedCache? cache, CollectorOptions? options)
        {
            _options = options ?? new CollectorOptions();
            _store = new CrawlStateStore(cache ?? new InMemoryDistributedCache());
            _httpClient = _options.HttpClient ?? Common.HttpClient;
        }

        public async Task<IDictionary<string, ICollection<Team>>> Collect(IDictionary<string, string> tierToLinks, bool prefixUsage)
        {
            // Sequential on purpose: all forums live on one host, so parallel forums would not be faster
            // under the per-host request limit, only harder to follow.
            var collectedTeams = new Dictionary<string, ICollection<Team>>();
            foreach (var kv in tierToLinks)
            {
                await CollectFromForum(collectedTeams, kv.Key, kv.Value, prefixUsage).ConfigureAwait(false);
            }
            return collectedTeams;
        }

        public async Task CollectFromForum(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage)
        {
            var forum = _store.GetForum(url) ?? new ForumCrawlState();
            var incremental = forum.Complete && !_options.FullRefresh;
            var oldWatermark = forum.Watermark;
            var newWatermark = oldWatermark;

            // Listings are sorted by latest post. Once a non-sticky thread is not newer than the last run,
            // every thread after it is unchanged too.
            var listed = new List<ThreadListItem>();
            var pages = 1;
            var listingPages = 0;
            var walkedAllPages = true;
            for (var page = 1; page <= pages; page++)
            {
                var pageUrl = SmogonPages.PageUrl(url, page);
                var site = await GetPageAsync(pageUrl).ConfigureAwait(false);
                SmogonPages.EnsureTemplate(site, pageUrl, "forum_view");
                listingPages++;
                if (page == 1)
                {
                    pages = SmogonPages.GetNumberOfPages(site);
                }

                var reachedKnownThreads = false;
                foreach (var item in ForumListingParser.Parse(site))
                {
                    listed.Add(item);
                    newWatermark = Math.Max(newWatermark, item.LatestPostTimestamp);
                    if (incremental && !item.Sticky && item.LatestPostTimestamp <= oldWatermark)
                    {
                        reachedKnownThreads = true;
                    }
                }
                if (reachedKnownThreads)
                {
                    walkedAllPages = page == pages;
                    break;
                }
            }

            var updatedThreads = 0;
            var seen = new HashSet<long>();
            var toUpdate = new List<ThreadListItem>();
            foreach (var item in listed)
            {
                // The same thread can show up twice if the listing shifted while paging.
                if (seen.Add(item.ThreadId))
                {
                    forum.Threads[item.ThreadId] = new ForumThreadEntry { Url = item.Url, Prefix = item.Prefix };
                    toUpdate.Add(item);
                }
            }
            toUpdate.AddRange(forum.RetryThreads.Values.Where(item => !seen.Contains(item.ThreadId)));

            foreach (var item in toUpdate)
            {
                var result = await UpdateThreadAsync(item, url).ConfigureAwait(false);
                if (result == ThreadUpdate.Updated)
                {
                    updatedThreads++;
                }
                if (result == ThreadUpdate.Failed)
                {
                    forum.RetryThreads[item.ThreadId] = item;
                }
                else
                {
                    forum.RetryThreads.Remove(item.ThreadId);
                }
            }

            if (walkedAllPages)
            {
                // Threads missing from a complete listing were deleted or moved away.
                foreach (var threadId in forum.Threads.Keys.Where(id => !seen.Contains(id)).ToList())
                {
                    forum.Threads.Remove(threadId);
                    forum.RetryThreads.Remove(threadId);
                }
            }

            var teamCount = 0;
            foreach (var (threadId, entry) in forum.Threads.ToList())
            {
                var state = _store.GetThread(threadId);
                if (state == null)
                {
                    continue;
                }
                if (state.ForumUrl.Length > 0 && state.ForumUrl != url)
                {
                    // Moved to another forum, which lists it from now on
                    forum.Threads.Remove(threadId);
                    continue;
                }

                var identifier = prefixUsage ? entry.Prefix : tier;
                if (!collectedTeams.TryGetValue(identifier, out var teams))
                {
                    teams = new List<Team>();
                    collectedTeams.Add(identifier, teams);
                }
                foreach (var page in state.Pages.OrderBy(kv => kv.Key))
                {
                    foreach (var team in page.Value.Teams)
                    {
                        teams.Add(team);
                        teamCount++;
                    }
                }
            }

            forum.Watermark = newWatermark;
            forum.Complete = true;
            forum.LastCrawl = DateTime.UtcNow;
            _store.SetForum(url, forum);

            Console.WriteLine($"{tier}: {listingPages} listing page(s) read, {updatedThreads} thread(s) updated, {forum.Threads.Count} thread(s) known, {teamCount} team(s)");
        }

        private enum ThreadUpdate
        {
            Unchanged,
            Updated,
            /// <summary>The thread is gone or not accessible (404, 403, 410).</summary>
            Gone,
            /// <summary>The thread could not be read and is tried again next run.</summary>
            Failed,
        }

        /// <summary>Brings the stored state of a listed thread up to date.</summary>
        private async Task<ThreadUpdate> UpdateThreadAsync(ThreadListItem item, string forumUrl)
        {
            var state = _store.GetThread(item.ThreadId);
            var migrated = false;
            if (state == null)
            {
                state = MigrateLegacyState(item);
                migrated = state != null;
            }

            var changed = _options.FullRefresh
                || state == null
                || state.NumberOfPages == 0
                || state.LatestPostTimestamp != item.LatestPostTimestamp;
            if (!changed)
            {
                if (migrated || state!.ForumUrl != forumUrl || state.Url != item.Url)
                {
                    state!.ForumUrl = forumUrl;
                    state.Url = item.Url;
                    _store.SetThread(item.ThreadId, state);
                }
                return ThreadUpdate.Unchanged;
            }

            state ??= new ThreadCrawlState();
            var listedLastPage = Math.Max(1, item.LastPage);
            var firstPage = _options.FullRefresh || state.NumberOfPages == 0
                ? 1
                : Math.Min(state.NumberOfPages, listedLastPage);

            var pagesToRead = new List<int>();
            if (_options.RecentPageRefreshWindow > TimeSpan.Zero)
            {
                var recentSince = DateTime.Now - _options.RecentPageRefreshWindow;
                pagesToRead.AddRange(state.Pages
                    .Where(kv => kv.Key < firstPage && kv.Value.NewestPostDate >= recentSince)
                    .Select(kv => kv.Key)
                    .OrderBy(page => page));
            }
            for (var page = firstPage; page <= listedLastPage; page++)
            {
                pagesToRead.Add(page);
            }

            Console.WriteLine($"Reading {item.Url} (page {FormatPages(pagesToRead)} of {listedLastPage})");

            var numberOfPages = listedLastPage;
            for (var i = 0; i < pagesToRead.Count; i++)
            {
                var page = pagesToRead[i];
                if (page > numberOfPages)
                {
                    break;
                }

                var pageUrl = SmogonPages.PageUrl(item.Url, page);
                string site;
                try
                {
                    site = await GetPageAsync(pageUrl).ConfigureAwait(false);
                }
                catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
                {
                    Console.WriteLine($"Skipping {pageUrl}: HTTP {(int)e.StatusCode!}");
                    return ThreadUpdate.Gone;
                }
                try
                {
                    SmogonPages.EnsureThreadPage(site, pageUrl);
                    _unexpectedThreadPagesInARow = 0;
                }
                catch (UnexpectedPageException e)
                {
                    // A single odd thread is skipped and retried next run; many in a row mean the markup changed, and the crawl stops.
                    if (++_unexpectedThreadPagesInARow >= MaxUnexpectedThreadPagesInARow)
                    {
                        throw;
                    }
                    Console.WriteLine("Skipping thread: " + e.Message);
                    return ThreadUpdate.Failed;
                }

                numberOfPages = SmogonPages.GetNumberOfPages(site);
                if (page > numberOfPages)
                {
                    // Posts were deleted and the requested page no longer exists; Smogon served the last page instead.
                    // Read the real last page next (unless it was read already); the loop then stops at the missing pages.
                    if (!pagesToRead.Contains(numberOfPages))
                    {
                        pagesToRead.Insert(i + 1, numberOfPages);
                    }
                    continue;
                }
                if (i == pagesToRead.Count - 1 && numberOfPages > page)
                {
                    // More pages than the listing showed
                    for (var extra = page + 1; extra <= numberOfPages; extra++)
                    {
                        pagesToRead.Add(extra);
                    }
                }

                state.Pages[page] = await AnalyzePageAsync(item.Url, page, site, item.Prefix).ConfigureAwait(false);
            }

            foreach (var stalePage in state.Pages.Keys.Where(page => page > numberOfPages).ToList())
            {
                state.Pages.Remove(stalePage);
            }
            state.NumberOfPages = numberOfPages;
            state.LatestPostTimestamp = item.LatestPostTimestamp;
            state.Url = item.Url;
            state.ForumUrl = forumUrl;
            _store.SetThread(item.ThreadId, state);
            return ThreadUpdate.Updated;
        }

        [GeneratedRegex("/page-(\\d+)#")]
        private static partial Regex TeamUrlPageRegex();

        /// <summary>
        /// Converts an entry of crawler versions before 3.0 into the new state. If the latest post and the page count still
        /// match the listing, the thread counts as unchanged and is not read at all; otherwise it is read from its last known page on.
        /// </summary>
        private ThreadCrawlState? MigrateLegacyState(ThreadListItem item)
        {
            var legacy = _store.GetLegacyThread(item.Url);
            if (legacy?.CollectedTeams == null)
            {
                return null;
            }

            var state = new ThreadCrawlState
            {
                Url = item.Url,
                NumberOfPages = Math.Max(1, legacy.NumberOfPages),
            };
            foreach (var team in legacy.CollectedTeams)
            {
                var pageMatch = TeamUrlPageRegex().Match(team.URL ?? "");
                var page = pageMatch.Success ? int.Parse(pageMatch.Groups[1].Value) : 1;
                if (!state.Pages.TryGetValue(page, out var pageState))
                {
                    pageState = new PageCrawlState();
                    state.Pages[page] = pageState;
                }
                pageState.Teams.Add(team);
                if (team.PostDate > pageState.NewestPostDate)
                {
                    pageState.NewestPostDate = team.PostDate;
                }
            }

            if (legacy.NumberOfPages == item.LastPage
                && TryParseDisplayDate(item.LatestPostDisplayDate, out var latestPost)
                && latestPost == legacy.LastPost)
            {
                state.LatestPostTimestamp = item.LatestPostTimestamp;
            }
            return state;
        }

        private static bool TryParseDisplayDate(string displayDate, out DateTime date) =>
            DateTime.TryParseExact(displayDate.Replace("at ", ""), "MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out date);

        private static string FormatPages(IList<int> pages)
        {
            var parts = new List<string>();
            for (var i = 0; i < pages.Count; i++)
            {
                var start = pages[i];
                while (i + 1 < pages.Count && pages[i + 1] == pages[i] + 1)
                {
                    i++;
                }
                parts.Add(start == pages[i] ? start.ToString() : start + "-" + pages[i]);
            }
            return string.Join(", ", parts);
        }

        /// <summary>Reads a whole thread, ignoring any stored state.</summary>
        public async Task<ThreadAnalyzeResult> AnalyzeThread(string url, string? prefix)
        {
            var pages = 1;
            var latestPost = DateTime.UnixEpoch;
            var collectedTeams = new List<Team>();
            try
            {
                for (var pageCount = 1; pageCount <= pages; pageCount++)
                {
                    var pageUrl = SmogonPages.PageUrl(url, pageCount);
                    var site = await GetPageAsync(pageUrl).ConfigureAwait(false);
                    SmogonPages.EnsureThreadPage(site, pageUrl);
                    if (pageCount == 1)
                    {
                        pages = SmogonPages.GetNumberOfPages(site);
                    }

                    var lineDataHandler = await ParsePageAsync(url, pageCount, site, prefix, collectedTeams).ConfigureAwait(false);
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

        private async Task<PageCrawlState> AnalyzePageAsync(string url, int pageCount, string site, string? prefix)
        {
            var teams = new List<Team>();
            var lineDataHandler = await ParsePageAsync(url, pageCount, site, prefix, teams).ConfigureAwait(false);
            return new PageCrawlState
            {
                Teams = teams,
                NewestPostDate = lineDataHandler.NewestPostDate,
                FetchedAt = DateTime.UtcNow,
            };
        }

        private async Task<LineDataHandler> ParsePageAsync(string url, int pageCount, string site, string? prefix, ICollection<Team> collectedTeams)
        {
            var lineDataHandler = new LineDataHandler();
            var currentTeams = new List<string>();
            foreach (var line in site.Split('\n'))
            {
                await HandleLine(url, collectedTeams, pageCount, currentTeams, line, prefix, lineDataHandler).ConfigureAwait(false);
            }
            return lineDataHandler;
        }

        private Task<string> GetPageAsync(string url) => _httpClient.GetStringAsync(url);

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
                if (lineDataHandler.PostDate > lineDataHandler.NewestPostDate)
                {
                    lineDataHandler.NewestPostDate = lineDataHandler.PostDate;
                }
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

        private Task<string> GetTeamFromPokepasteURL(string pasteUrl) => GetPasteAsync(pasteUrl, ExtractPokepasteTeam);

        private Task<string> GetTeamFromBinURL(string pasteUrl) => GetPasteAsync(pasteUrl, site => htmlRemoverRegex.Replace(site, string.Empty));

        /// <summary>
        /// Pastes hardly ever change, so each one is fetched only once. Only team texts are kept;
        /// "" marks a paste without team. Failed requests are not stored and get retried in a later run.
        /// </summary>
        private async Task<string> GetPasteAsync(string pasteUrl, Func<string, string> extractText)
        {
            var cached = _store.GetPaste(pasteUrl);
            if (cached != null)
            {
                return cached;
            }

            string site;
            try
            {
                site = await _httpClient.GetStringAsync(pasteUrl).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    _store.SetPaste(pasteUrl, "");
                }
                return "";
            }

            var text = extractText(site);
            _store.SetPaste(pasteUrl, IsTeam(text) ? text : "");
            return text;
        }

        private string ExtractPokepasteTeam(string site)
        {
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
