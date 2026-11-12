using Microsoft.Extensions.Caching.Distributed;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>
    /// Collects teams from Smogon forums. With a persistent cache, a run only reads what changed since the last run.
    /// Team posts are found through Smogon's search (<see cref="CollectorOptions.UseSearch"/>): the first run searches all
    /// posts of a forum, later runs the posts since the last run, and only the pages with hits are read. Forums the search
    /// cannot cover are walked: the first walk reads every thread, later walks go up to the first known, unchanged thread
    /// and read the changed threads from their last known page on (plus pages with recent posts, see
    /// <see cref="CollectorOptions.RecentPageRefreshWindow"/>).
    /// </summary>
    public partial class TeamCollector : ICollector
    {
        private const int MaxUnexpectedThreadPagesInARow = 5;

        /// <summary>
        /// Pages that may wait for their pastes while the next pages are read. About a minute of reading ahead, enough for
        /// most bursts of pastes, while the memory stays bounded (a waiting page holds its HTML, about 1 MB).
        /// </summary>
        private const int MaxPagesWaitingForPastes = 25;

        private readonly CrawlStateStore _store;
        private readonly CollectorOptions _options;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _pagesWaitingForPastes = new(MaxPagesWaitingForPastes);
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _pastesInFlight = new();
        private readonly Lazy<Task<ShowdownNames>> _showdownNames;
        private readonly PostSearch _postSearch;
        private int _unexpectedThreadPagesInARow;
        private bool _searchUnavailable;

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
            _showdownNames = new Lazy<Task<ShowdownNames>>(() => ShowdownNames.LoadAsync(_httpClient));
            _postSearch = new PostSearch(GetPageAsync);
        }

        public async Task<IDictionary<string, ICollection<Team>>> Collect(IDictionary<string, string> tierToLinks, bool prefixUsage)
        {
            // Sequential on purpose: all forums live on one host, so parallel forums would not be faster
            // under the per-host request limit, only harder to follow.
            var collectedTeams = new Dictionary<string, ICollection<Team>>();
            var sinceLastRuns = await SearchSinceLastRunsAsync(tierToLinks).ConfigureAwait(false);
            foreach (var kv in tierToLinks)
            {
                await CollectFromForumAsync(collectedTeams, kv.Key, kv.Value, prefixUsage, sinceLastRuns).ConfigureAwait(false);
            }
            return collectedTeams;
        }

        public Task CollectFromForum(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage) =>
            CollectFromForumAsync(collectedTeams, tier, url, prefixUsage, sinceLastRuns: null);

        private async Task CollectFromForumAsync(IDictionary<string, ICollection<Team>> collectedTeams, string tier, string url, bool prefixUsage, ForumSearch? sinceLastRuns)
        {
            var forum = _store.GetForum(url) ?? new ForumCrawlState();
            var crawlStarted = DateTime.UtcNow;

            string? summary = null;
            switch (GetDiscovery(tier, forum))
            {
                case Discovery.FirstSearch:
                    if (await GetNodeIdAsync(forum, url).ConfigureAwait(false) is long newNodeId
                        && await SearchForumsAsync([newNodeId], since: null).ConfigureAwait(false) is { } all
                        && all.Covers(newNodeId, since: null)
                        && !HasLegacyThreads(all.Result.Hits))
                    {
                        // Posts are covered up to the search, not up to now.
                        crawlStarted = all.StartedAt;
                        summary = await CollectBySearchAsync(forum, url, newNodeId, all.Result.Hits, firstRun: true).ConfigureAwait(false);
                        // A later listing walk stops at the threads without posts since.
                        forum.Watermark = new DateTimeOffset(all.StartedAt).ToUnixTimeSeconds();
                    }
                    break;
                case Discovery.LaterSearch when forum.LastCrawl != default && NodeId(forum, url) is long nodeId:
                    var since = SearchStart(forum.LastCrawl);
                    var recent = sinceLastRuns != null && sinceLastRuns.NodeIds.Contains(nodeId)
                        ? sinceLastRuns
                        : await SearchForumsAsync([nodeId], since).ConfigureAwait(false);
                    if (recent != null && recent.Covers(nodeId, since))
                    {
                        crawlStarted = recent.StartedAt;
                        summary = await CollectBySearchAsync(forum, url, nodeId, recent.Result.Hits, firstRun: false).ConfigureAwait(false);
                    }
                    break;
            }
            summary ??= await CollectByListingAsync(forum, url, incremental: forum.Complete && !_options.FullRefresh).ConfigureAwait(false);

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

            forum.Complete = true;
            forum.LastCrawl = crawlStarted;
            _store.SetForum(url, forum);

            Console.WriteLine($"{tier}: {summary}, {forum.Threads.Count} thread(s) known, {teamCount} team(s)");
        }

        private enum Discovery
        {
            /// <summary>Walk the forum listing.</summary>
            Listing,
            /// <summary>Search all posts of a forum crawled for the first time.</summary>
            FirstSearch,
            /// <summary>Search the posts since the last run.</summary>
            LaterSearch,
        }

        private Discovery GetDiscovery(string tier, ForumCrawlState forum)
        {
            if (!_options.UseSearch || _options.FullRefresh || _options.SearchQueries.Count == 0
                || _options.ListingForums.Any(name => tier.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                return Discovery.Listing;
            }
            return forum.Complete ? Discovery.LaterSearch : Discovery.FirstSearch;
        }

        private static long? NodeId(ForumCrawlState forum, string url) => forum.NodeId != 0 ? forum.NodeId : SmogonPages.GetForumId(url);

        /// <summary>The node ID of a forum; a forum without one in its URL (".../forums/gen1/") has it on its listing page.</summary>
        private async Task<long?> GetNodeIdAsync(ForumCrawlState forum, string url)
        {
            if (NodeId(forum, url) is long known)
            {
                return known;
            }
            var site = await GetPageAsync(url).ConfigureAwait(false);
            SmogonPages.EnsureTemplate(site, url, "forum_view");
            forum.NodeId = SmogonPages.GetNodeId(site) ?? 0;
            return forum.NodeId != 0 ? forum.NodeId : null;
        }

        /// <summary>The search for posts since a run starts a day early: Smogon reads the dates in its own time zone.</summary>
        private static DateTime SearchStart(DateTime lastCrawl) => lastCrawl.Date.AddDays(-1);

        /// <summary>A search of forums, with the time it started: the forums are covered up to then.</summary>
        private sealed record ForumSearch(IReadOnlyCollection<long> NodeIds, DateTime? Since, DateTime StartedAt, SearchResult Result)
        {
            /// <summary>Whether the search found all posts of the forum from <paramref name="since"/> on (all of them for null).</summary>
            public bool Covers(long nodeId, DateTime? since) =>
                NodeIds.Contains(nodeId) && !Result.Uncovered.Contains(nodeId) && (Since == null || Since <= since);
        }

        /// <summary>Null if the search is not available; then the forums of the rest of the run are walked.</summary>
        private async Task<ForumSearch?> SearchForumsAsync(IReadOnlyList<long> nodeIds, DateTime? since)
        {
            if (_searchUnavailable)
            {
                return null;
            }
            var startedAt = DateTime.UtcNow;
            var result = await _postSearch.SearchAsync(_options.SearchQueries, nodeIds, since).ConfigureAwait(false);
            if (result == null)
            {
                _searchUnavailable = true;
                return null;
            }
            return new ForumSearch(nodeIds, since, startedAt, result);
        }

        /// <summary>
        /// One search for the posts since the last run of all forums crawled before, from the oldest last run on.
        /// Null if there are none (or no search).
        /// </summary>
        private async Task<ForumSearch?> SearchSinceLastRunsAsync(IDictionary<string, string> tierToLinks)
        {
            var forums = new List<(long NodeId, DateTime LastCrawl)>();
            foreach (var (tier, url) in tierToLinks)
            {
                var forum = _store.GetForum(url);
                if (forum != null && forum.LastCrawl != default && GetDiscovery(tier, forum) == Discovery.LaterSearch && NodeId(forum, url) is long nodeId)
                {
                    forums.Add((nodeId, forum.LastCrawl));
                }
            }
            if (forums.Count == 0)
            {
                return null;
            }
            return await SearchForumsAsync(forums.Select(f => f.NodeId).Distinct().ToList(), SearchStart(forums.Min(f => f.LastCrawl))).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether hits are in threads of a state of crawler version 2.x. Such a forum is walked: the walk takes the threads
        /// over that did not change since and reads only the others.
        /// </summary>
        private bool HasLegacyThreads(IReadOnlyList<SearchHit> hits)
        {
            var legacy = hits.Select(hit => hit.ThreadUrl).Distinct().FirstOrDefault(threadUrl => _store.GetLegacyThread(threadUrl) != null);
            if (legacy != null)
            {
                Console.WriteLine($"{legacy} is stored by crawler version 2.x; the forum listing is walked to take its threads over.");
            }
            return legacy != null;
        }

        private sealed record SearchedThread(string Url, string Prefix, List<SearchHit> Hits);

        /// <summary>
        /// Reads the pages of the forum's search hits that were not read since the post, and pages whose likes are due
        /// (<see cref="RefreshDue"/>). Threads that failed before are read as in a listing walk. On the <paramref name="firstRun"/>,
        /// the search covers all pages a thread had: a later listing walk reads on from its last page.
        /// </summary>
        private async Task<string> CollectBySearchAsync(ForumCrawlState forum, string url, long nodeId, IReadOnlyList<SearchHit> hits, bool firstRun)
        {
            forum.NodeId = nodeId;
            // A forum without node ID in its URL is shown with its slug; renamed ones with an ID by their new one.
            var forumHits = hits.Where(hit => hit.ForumUrl == url || SmogonPages.GetForumId(hit.ForumUrl) == nodeId).ToList();
            var jobs = new Dictionary<long, SearchedThread>();
            foreach (var threadHits in forumHits.GroupBy(hit => hit.ThreadId))
            {
                var newest = threadHits.MaxBy(hit => hit.PostDate)!;
                var known = forum.Threads.GetValueOrDefault(threadHits.Key);
                var prefix = newest.Prefix.Length > 0 ? newest.Prefix : known?.Prefix ?? "";
                forum.Threads[threadHits.Key] = new ForumThreadEntry { Url = newest.ThreadUrl, Prefix = prefix };

                var state = _store.GetThread(threadHits.Key);
                var unread = threadHits
                    .Where(hit => state == null || !state.Pages.TryGetValue(hit.Page, out var page) || page.FetchedAt <= hit.PostDate)
                    .ToList();
                if (unread.Count > 0)
                {
                    jobs[threadHits.Key] = new SearchedThread(newest.ThreadUrl, prefix, unread);
                }
                else if (state != null && (state.ForumUrl != url || state.Url != newest.ThreadUrl))
                {
                    // Moved here or renamed
                    state.ForumUrl = url;
                    state.Url = newest.ThreadUrl;
                    _store.SetThread(threadHits.Key, state);
                }
            }

            var duePages = new Dictionary<long, List<int>>();
            if (_options.RecentPageRefreshWindow > TimeSpan.Zero)
            {
                var now = DateTime.UtcNow;
                foreach (var (threadId, entry) in forum.Threads)
                {
                    var due = _store.GetThread(threadId)?.Pages.Where(kv => RefreshDue(kv.Value, now)).Select(kv => kv.Key).ToList();
                    if (due?.Count > 0)
                    {
                        duePages[threadId] = due;
                        jobs.TryAdd(threadId, new SearchedThread(entry.Url, entry.Prefix, []));
                    }
                }
            }

            var updatedThreads = 0;
            var stored = new List<Task>();
            try
            {
                foreach (var (threadId, job) in jobs)
                {
                    var pages = job.Hits.Select(hit => hit.Page).Concat(duePages.GetValueOrDefault(threadId) ?? []).Distinct().Order().ToList();
                    var (result, threadStored) = await UpdateSearchedThreadAsync(threadId, job, pages, url, firstRun).ConfigureAwait(false);
                    stored.Add(threadStored);
                    if (result == ThreadUpdate.Updated)
                    {
                        updatedThreads++;
                    }
                    if (result == ThreadUpdate.Failed)
                    {
                        // Its latest post is unknown (0), so the retry reads it.
                        forum.RetryThreads[threadId] = new ThreadListItem { ThreadId = threadId, Url = job.Url, Prefix = job.Prefix, LastPage = pages.Max() };
                    }
                }
                foreach (var item in forum.RetryThreads.Values.Where(item => !jobs.ContainsKey(item.ThreadId)).ToList())
                {
                    var (result, threadStored) = await UpdateThreadAsync(item, url).ConfigureAwait(false);
                    stored.Add(threadStored);
                    if (result == ThreadUpdate.Updated)
                    {
                        updatedThreads++;
                    }
                    if (result != ThreadUpdate.Failed)
                    {
                        forum.RetryThreads.Remove(item.ThreadId);
                    }
                }
            }
            catch
            {
                await WhenAllIgnoringErrors(stored).ConfigureAwait(false);
                throw;
            }
            await Task.WhenAll(stored).ConfigureAwait(false);

            return $"{forumHits.Count} search hit(s), {updatedThreads} thread(s) updated";
        }

        /// <summary>
        /// In search runs, a page with teams younger than <see cref="CollectorOptions.RecentPageRefreshWindow"/> is read again
        /// once its newest team is twice as old as at the last read. Likes come in early, so the reads thin out.
        /// </summary>
        private bool RefreshDue(PageCrawlState page, DateTime now)
        {
            if (page.Teams.Count == 0)
            {
                return false;
            }
            var posted = page.Teams.Max(team => team.PostDate);
            var age = now - posted;
            return age <= _options.RecentPageRefreshWindow && age >= 2 * (page.FetchedAt - posted);
        }

        /// <summary>Walks the listing of a forum and updates the threads with new posts.</summary>
        private async Task<string> CollectByListingAsync(ForumCrawlState forum, string url, bool incremental)
        {
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
                    forum.NodeId = SmogonPages.GetNodeId(site) ?? forum.NodeId;
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

            var stored = new List<Task>();
            try
            {
                foreach (var item in toUpdate)
                {
                    // Threads to retry are read even without new posts.
                    var coveredUntil = incremental && !forum.RetryThreads.ContainsKey(item.ThreadId) ? oldWatermark : 0;
                    var (result, threadStored) = await UpdateThreadAsync(item, url, coveredUntil).ConfigureAwait(false);
                    stored.Add(threadStored);
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
            }
            catch
            {
                // The crawl stops. Threads read before are still stored, once their pastes are in.
                await WhenAllIgnoringErrors(stored).ConfigureAwait(false);
                throw;
            }
            // The teams below come from the stored threads.
            await Task.WhenAll(stored).ConfigureAwait(false);

            if (walkedAllPages)
            {
                // Threads missing from a complete listing were deleted or moved away.
                foreach (var threadId in forum.Threads.Keys.Where(id => !seen.Contains(id)).ToList())
                {
                    forum.Threads.Remove(threadId);
                    forum.RetryThreads.Remove(threadId);
                }
            }

            forum.Watermark = newWatermark;
            return $"{listingPages} listing page(s) read, {updatedThreads} thread(s) updated";
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

        /// <summary>
        /// Brings the stored state of a listed thread up to date. Returns once all pages are read; the pastes of the last
        /// pages may still be on their way, and <c>Stored</c> completes when the thread is stored with them.
        /// </summary>
        /// <param name="coveredUntil">Posts up to this timestamp were covered by the last run: a thread whose latest post is
        /// not newer and that the first search found no teams in (no state), or whose latest post the search left unknown,
        /// is not read.</param>
        private async Task<(ThreadUpdate Result, Task Stored)> UpdateThreadAsync(ThreadListItem item, string forumUrl, long coveredUntil = 0)
        {
            var state = _store.GetThread(item.ThreadId);
            var covered = !_options.FullRefresh && item.LatestPostTimestamp != 0 && item.LatestPostTimestamp <= coveredUntil
                && (state == null || state.LatestPostTimestamp == 0);
            if (covered && state == null)
            {
                return (ThreadUpdate.Unchanged, Task.CompletedTask);
            }
            var migrated = false;
            if (state == null)
            {
                state = MigrateLegacyState(item);
                migrated = state != null;
            }

            // A latest post of 0 is unknown: a thread to retry after a search run.
            var changed = !covered && (_options.FullRefresh
                || state == null
                || state.NumberOfPages == 0
                || item.LatestPostTimestamp == 0
                || state.LatestPostTimestamp != item.LatestPostTimestamp);
            if (!changed)
            {
                if (migrated || state!.ForumUrl != forumUrl || state.Url != item.Url)
                {
                    state!.ForumUrl = forumUrl;
                    state.Url = item.Url;
                    _store.SetThread(item.ThreadId, state);
                }
                return (ThreadUpdate.Unchanged, Task.CompletedTask);
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

            var read = await ReadPagesAsync(item.Url, item.Prefix, pagesToRead, listedLastPage, extendToEnd: true).ConfigureAwait(false);
            if (read.Result != ThreadUpdate.Updated)
            {
                return (read.Result, Task.CompletedTask);
            }
            return (ThreadUpdate.Updated, StoreThreadAsync(item.ThreadId, item.Url, forumUrl, state, read.NumberOfPages, read.Analyses, item.LatestPostTimestamp));
        }

        /// <summary>
        /// Reads the given pages of a thread found by search; see <see cref="UpdateThreadAsync"/> for the result. On the
        /// <paramref name="firstRun"/>, the thread's pages count as read up to its current last page.
        /// </summary>
        private async Task<(ThreadUpdate Result, Task Stored)> UpdateSearchedThreadAsync(long threadId, SearchedThread job, List<int> pages, string forumUrl, bool firstRun)
        {
            var state = _store.GetThread(threadId) ?? new ThreadCrawlState();
            Console.WriteLine($"Reading {job.Url} (page {FormatPages(pages)}, found by search)");

            var expectedPosts = job.Hits
                .Where(hit => hit.PostId != null)
                .GroupBy(hit => hit.Page)
                .ToDictionary(group => group.Key, group => group.Select(hit => hit.PostId!.Value).ToList());
            var read = await ReadPagesAsync(job.Url, job.Prefix, pages, int.MaxValue, extendToEnd: false, expectedPosts).ConfigureAwait(false);
            if (read.Result != ThreadUpdate.Updated)
            {
                return (read.Result, Task.CompletedTask);
            }

            // Posts deleted between search and read move a post to an earlier page; its link leads to where it is now.
            var numberOfPages = read.NumberOfPages;
            foreach (var postId in read.MissingPosts)
            {
                var postUrl = job.Url + "post-" + postId;
                var (site, failure) = await GetThreadPageAsync(postUrl).ConfigureAwait(false);
                if (failure != null)
                {
                    // Deleted, or the odd page is counted towards stopping the crawl
                    continue;
                }
                var page = SmogonPages.GetCurrentPage(site!);
                numberOfPages = SmogonPages.GetNumberOfPages(site!);
                if (!read.Analyses.Any(analysis => analysis.Page == page))
                {
                    Console.WriteLine($"Post {postId} is on page {page} now");
                    read.Analyses.Add((page, await StartPageAnalysisAsync(job.Url, page, site!, job.Prefix).ConfigureAwait(false)));
                }
            }
            // The latest post stays unknown (0): a listing walk reads the last page again, and the pages after it.
            return (ThreadUpdate.Updated, StoreThreadAsync(threadId, job.Url, forumUrl, state, numberOfPages, read.Analyses, latestPostTimestamp: firstRun ? 0 : null));
        }

        private sealed record PagesRead(ThreadUpdate Result, int NumberOfPages, List<(int Page, Task<PageCrawlState> State)> Analyses, List<long> MissingPosts);

        /// <summary>
        /// Reads pages of a thread in order and starts their analysis. A page beyond the end (posts were deleted) is replaced
        /// by the real last page; with <paramref name="extendToEnd"/>, the pages the thread grew by are read as well.
        /// Posts of <paramref name="expectedPosts"/> that are not on their page are returned.
        /// </summary>
        private async Task<PagesRead> ReadPagesAsync(string threadUrl, string? prefix, List<int> pagesToRead, int numberOfPages, bool extendToEnd, Dictionary<int, List<long>>? expectedPosts = null)
        {
            var analyses = new List<(int Page, Task<PageCrawlState> State)>();
            var missingPosts = new List<long>();
            var complete = false;
            try
            {
                for (var i = 0; i < pagesToRead.Count; i++)
                {
                    var page = pagesToRead[i];
                    if (page > numberOfPages)
                    {
                        // The thread got shorter: the posts expected there are elsewhere now.
                        missingPosts.AddRange(pagesToRead.Skip(i).SelectMany(rest => expectedPosts?.GetValueOrDefault(rest) ?? []));
                        break;
                    }

                    var pageUrl = SmogonPages.PageUrl(threadUrl, page);
                    var (site, failure) = await GetThreadPageAsync(pageUrl).ConfigureAwait(false);
                    if (failure != null)
                    {
                        return new PagesRead(failure.Value, numberOfPages, [], []);
                    }

                    numberOfPages = SmogonPages.GetNumberOfPages(site!);
                    if (page > numberOfPages)
                    {
                        // Posts were deleted and the requested page no longer exists; Smogon served the last page instead.
                        // Read the real last page next (unless it was read already); the loop then stops at the missing pages.
                        if (!pagesToRead.Contains(numberOfPages))
                        {
                            pagesToRead.Insert(i + 1, numberOfPages);
                        }
                        if (expectedPosts != null && expectedPosts.Remove(page, out var moved))
                        {
                            expectedPosts[numberOfPages] = [.. expectedPosts.GetValueOrDefault(numberOfPages) ?? [], .. moved];
                        }
                        continue;
                    }
                    if (extendToEnd && i == pagesToRead.Count - 1 && numberOfPages > page)
                    {
                        // More pages than the listing showed
                        for (var extra = page + 1; extra <= numberOfPages; extra++)
                        {
                            pagesToRead.Add(extra);
                        }
                    }
                    if (expectedPosts != null && expectedPosts.TryGetValue(page, out var posts))
                    {
                        missingPosts.AddRange(posts.Where(postId => !site!.Contains($"data-content=\"post-{postId}\"")));
                    }

                    analyses.Add((page, await StartPageAnalysisAsync(threadUrl, page, site!, prefix).ConfigureAwait(false)));
                }
                complete = true;
            }
            finally
            {
                if (!complete)
                {
                    // The thread is not stored; the pastes its pages are still fetching are kept all the same.
                    analyses.ForEach(analysis => IgnoreErrors(analysis.State));
                }
            }
            return new PagesRead(ThreadUpdate.Updated, numberOfPages, analyses, missingPosts);
        }

        /// <summary>A thread page, or why the thread is skipped: gone (404, 403, 410) or not a thread page.</summary>
        private async Task<(string? Site, ThreadUpdate? Failure)> GetThreadPageAsync(string pageUrl)
        {
            string site;
            try
            {
                site = await GetPageAsync(pageUrl).ConfigureAwait(false);
            }
            catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone)
            {
                Console.WriteLine($"Skipping {pageUrl}: HTTP {(int)e.StatusCode!}");
                return (null, ThreadUpdate.Gone);
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
                return (null, ThreadUpdate.Failed);
            }
            return (site, null);
        }

        /// <param name="latestPostTimestamp">The latest post of the listing, 0 (unknown) for a thread found by the first search
        /// of its forum. Null for a thread found by a later search, whose pages in between are still unread: a later listing
        /// walk then reads on from the last page it read itself.</param>
        private async Task StoreThreadAsync(long threadId, string threadUrl, string forumUrl, ThreadCrawlState state, int numberOfPages, List<(int Page, Task<PageCrawlState> State)> analyses, long? latestPostTimestamp)
        {
            foreach (var (page, analysis) in analyses)
            {
                state.Pages[page] = await analysis.ConfigureAwait(false);
            }
            foreach (var stalePage in state.Pages.Keys.Where(page => page > numberOfPages).ToList())
            {
                state.Pages.Remove(stalePage);
            }
            if (latestPostTimestamp is long timestamp)
            {
                state.NumberOfPages = numberOfPages;
                state.LatestPostTimestamp = timestamp;
            }
            else
            {
                state.NumberOfPages = Math.Min(state.NumberOfPages, numberOfPages);
            }
            state.Url = threadUrl;
            state.ForumUrl = forumUrl;
            _store.SetThread(threadId, state);
        }

        /// <summary>
        /// Starts parsing a page and returns without waiting for its pastes: they are fetched while the next Smogon pages
        /// are read (other hosts, so the request limit of either is unaffected). Waits while too many pages still wait for pastes.
        /// </summary>
        private async Task<Task<PageCrawlState>> StartPageAnalysisAsync(string url, int page, string site, string? prefix)
        {
            await _pagesWaitingForPastes.WaitAsync().ConfigureAwait(false);
            var fetchedAt = DateTime.UtcNow;
            return AnalyzeAsync();

            async Task<PageCrawlState> AnalyzeAsync()
            {
                try
                {
                    return await AnalyzePageAsync(url, page, site, prefix, fetchedAt).ConfigureAwait(false);
                }
                finally
                {
                    _pagesWaitingForPastes.Release();
                }
            }
        }

        private static void IgnoreErrors(Task task) => task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        private static async Task WhenAllIgnoringErrors(IEnumerable<Task> tasks)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // The error that stopped the crawl matters more.
            }
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

        private async Task<PageCrawlState> AnalyzePageAsync(string url, int pageCount, string site, string? prefix, DateTime fetchedAt)
        {
            var teams = new List<Team>();
            var lineDataHandler = await ParsePageAsync(url, pageCount, site, prefix, teams).ConfigureAwait(false);
            return new PageCrawlState
            {
                Teams = teams,
                NewestPostDate = lineDataHandler.NewestPostDate,
                FetchedAt = fetchedAt,
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
                if (line.Contains("psim.us/t/") || line.Contains("pokemonshowdown.com/"))
                {
                    foreach (var (teamId, password) in ShowdownTeams.FindLinks(line))
                    {
                        var showdownTeam = await GetShowdownTeamAsync(teamId, password).ConfigureAwait(false);
                        if (IsTeam(showdownTeam))
                        {
                            currentTeams.Add(showdownTeam);
                        }
                    }
                }
            }
            lineDataHandler.LastLine = line;
        }

        /// <summary>A team of Showdown's team site, stored like a paste. Its data files are only loaded for the first such team.</summary>
        private async Task<string> GetShowdownTeamAsync(string teamId, string? password)
        {
            var apiUrl = ShowdownTeams.ApiUrl(teamId, password);
            var cached = _store.GetPaste(apiUrl);
            if (cached != null)
            {
                return cached;
            }
            var names = await _showdownNames.Value.ConfigureAwait(false);
            return await GetPasteAsync(apiUrl, response => ShowdownTeams.FromApiResponse(response, names)).ConfigureAwait(false);
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
        private Task<string> GetPasteAsync(string pasteUrl, Func<string, string> extractText)
        {
            var cached = _store.GetPaste(pasteUrl);
            if (cached != null)
            {
                return Task.FromResult(cached);
            }
            // Pages are parsed side by side: a paste quoted on the next page may still be on its way.
            return _pastesInFlight.GetOrAdd(pasteUrl, url => new Lazy<Task<string>>(() => FetchPasteAsync(url, extractText))).Value;
        }

        private async Task<string> FetchPasteAsync(string pasteUrl, Func<string, string> extractText)
        {
            try
            {
                return await FetchAndStorePasteAsync(pasteUrl, extractText).ConfigureAwait(false);
            }
            finally
            {
                _pastesInFlight.TryRemove(pasteUrl, out _);
            }
        }

        private async Task<string> FetchAndStorePasteAsync(string pasteUrl, Func<string, string> extractText)
        {
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
