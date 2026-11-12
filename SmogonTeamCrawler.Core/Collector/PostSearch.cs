using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>A post found by Smogon's search. A thread hit (<see cref="PostId"/> null) is the first post of the thread.</summary>
    internal sealed record SearchHit(long ThreadId, string ThreadUrl, long? PostId, int Position, DateTime PostDate, string ForumUrl, string Prefix)
    {
        /// <summary>Posts per thread page on Smogon.</summary>
        public const int PostsPerPage = 25;

        /// <summary>The page of the thread the post is on, from its position ("Post #57" is on page 3).</summary>
        public int Page => Math.Max(1, (Position - 1) / PostsPerPage + 1);
    }

    /// <summary>The posts a search found, and the forums it could not cover: they have a day with more posts than a search shows.</summary>
    internal sealed record SearchResult(IReadOnlyList<SearchHit> Hits, IReadOnlySet<long> Uncovered);

    /// <summary>
    /// Finds posts of given forums through Smogon's guest search (XenForo), newest first. Smogon cuts a search off after
    /// 200 results; a search that reaches that is repeated for the days before its oldest result, so that the dates page
    /// through any number of posts.
    /// </summary>
    internal sealed partial class PostSearch
    {
        /// <summary>200 results, 10 per page: a search that fills this many pages may have been cut off.</summary>
        public const int MaxResultPages = 20;

        private const int MaxFloodWaits = 3;

        private readonly Func<string, Task<string>> _getPage;
        private readonly Func<TimeSpan, Task> _delay;

        public PostSearch(Func<string, Task<string>> getPage, Func<TimeSpan, Task>? delay = null)
        {
            _getPage = getPage;
            _delay = delay ?? (wait => Task.Delay(wait));
        }

        /// <summary>
        /// The search for posts matching <paramref name="query"/> in the forums <paramref name="nodeIds"/> (without their
        /// sub-forums), from <paramref name="since"/> on and before <paramref name="before"/>, newest first. The query is
        /// Smogon's syntax: words are all required, "|" is or, quotes make a phrase, parentheses group. Smogon reads the
        /// dates in its own time zone, so callers keep a day of margin.
        /// </summary>
        public static string Url(string query, IEnumerable<long> nodeIds, DateTime? since, DateTime? before) =>
            Common.SMOGON_FORUMS_URL + "search/1/?q=" + Uri.EscapeDataString(query) + "&t=post&o=date"
            + (since is DateTime start ? "&c[newer_than]=" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "")
            + (before is DateTime end ? "&c[older_than]=" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "")
            + string.Concat(nodeIds.Select((id, index) => $"&c[nodes][{index}]={id}"));

        /// <summary>
        /// All posts of the forums <paramref name="nodeIds"/> that match one of <paramref name="queries"/>, from
        /// <paramref name="since"/> on (all of them without) and before <paramref name="before"/>. Null if guests cannot search.
        /// </summary>
        public async Task<SearchResult?> SearchAsync(IEnumerable<string> queries, IReadOnlyList<long> nodeIds, DateTime? since, DateTime? before = null)
        {
            var hits = new Dictionary<(long ThreadId, long? PostId), SearchHit>();
            var uncovered = new HashSet<long>();
            var counts = new List<string>();
            foreach (var query in queries)
            {
                var known = hits.Count;
                if (!await SearchRangeAsync(query, nodeIds, since, before, hits, uncovered).ConfigureAwait(false))
                {
                    return null;
                }
                counts.Add($"\"{Shorten(query)}\" {hits.Count - known}");
            }
            var range = (since != null ? $" since {since:yyyy-MM-dd}" : "") + (before != null ? $" before {before:yyyy-MM-dd}" : "");
            Console.WriteLine($"Searched posts{range} in {nodeIds.Count} forum(s): {string.Join(", ", counts)}");
            return new SearchResult(hits.Values.ToList(), uncovered);
        }

        /// <summary>
        /// Pages through the results by date. A day with more results than a search shows is split by forum; a single forum
        /// with such a day is <paramref name="uncovered"/>. False if guests cannot search.
        /// </summary>
        private async Task<bool> SearchRangeAsync(string query, IReadOnlyList<long> nodeIds, DateTime? since, DateTime? before, Dictionary<(long, long?), SearchHit> hits, HashSet<long> uncovered)
        {
            var end = before;
            while (true)
            {
                var (found, cutOff) = await SearchPagesAsync(query, nodeIds, since, end).ConfigureAwait(false);
                if (found == null)
                {
                    return false;
                }
                foreach (var hit in found)
                {
                    hits.TryAdd((hit.ThreadId, hit.PostId), hit);
                }
                if (!cutOff)
                {
                    return true;
                }

                // Results come newest first: search again for the days up to the oldest result.
                var oldestDay = found.Count > 0 ? found.Min(hit => hit.PostDate).Date.AddDays(1) : (DateTime?)null;
                if (oldestDay != null && (end == null || oldestDay < end))
                {
                    end = oldestDay;
                    Console.WriteLine($"  \"{Shorten(query)}\": {hits.Count} post(s) so far, searching before {end:yyyy-MM-dd}");
                    continue;
                }
                if (nodeIds.Count > 1)
                {
                    var half = nodeIds.Count / 2;
                    return await SearchRangeAsync(query, nodeIds.Take(half).ToList(), since, end, hits, uncovered).ConfigureAwait(false)
                        && await SearchRangeAsync(query, nodeIds.Skip(half).ToList(), since, end, hits, uncovered).ConfigureAwait(false);
                }
                Console.WriteLine($"Forum {nodeIds[0]} has more than {MaxResultPages * 10} results for \"{Shorten(query)}\" on a single day; its listing is walked instead.");
                uncovered.Add(nodeIds[0]);
                return true;
            }
        }

        private static string Shorten(string query) => query.Length <= 40 ? query : query[..39] + "…";

        private async Task<(List<SearchHit>? Hits, bool CutOff)> SearchPagesAsync(string query, IReadOnlyList<long> nodeIds, DateTime? since, DateTime? before)
        {
            var hits = new List<SearchHit>();
            string? url = Url(query, nodeIds, since, before);
            var pages = 0;
            while (url != null && pages < MaxResultPages)
            {
                var html = await GetResultPageAsync(url).ConfigureAwait(false);
                if (html == null)
                {
                    return (null, false);
                }
                pages++;
                hits.AddRange(SearchResultParser.Parse(html, out url));
            }
            return (hits, pages >= MaxResultPages);
        }

        [GeneratedRegex(@"must wait at least (\d+) seconds")]
        private static partial Regex FloodWaitRegex();

        [GeneratedRegex("<div class=\"blockMessage[^\"]*\">\\s*([^<]+)")]
        private static partial Regex MessageRegex();

        /// <summary>A result page; null (after a message) if Smogon answers with anything else.</summary>
        private async Task<string?> GetResultPageAsync(string url)
        {
            for (var attempt = 0; ; attempt++)
            {
                string html;
                try
                {
                    html = await _getPage(url).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Search not available ({e.Message}); the forum listings are walked instead.");
                    return null;
                }
                if (SmogonPages.IsBotCheck(html))
                {
                    throw new SmogonAccessBlockedException(url);
                }
                if (html.Contains("data-template=\"search_results\""))
                {
                    return html;
                }
                // Searches in quick succession
                var flood = FloodWaitRegex().Match(html);
                if (flood.Success && attempt < MaxFloodWaits)
                {
                    await _delay(TimeSpan.FromSeconds(int.Parse(flood.Groups[1].Value) + 1)).ConfigureAwait(false);
                    continue;
                }
                // Such as "The search could not be completed because the search keywords were too short, too long, or too common."
                var message = MessageRegex().Matches(html).Select(match => match.Groups[1].Value.Trim())
                    .FirstOrDefault(text => !text.Contains("JavaScript") && !text.Contains("out of date browser"));
                Console.WriteLine($"Search not available ({message ?? url + " answered without results page"}); the forum listings are walked instead.");
                return null;
            }
        }
    }

    /// <summary>Reads the results of a XenForo search page ("search_results").</summary>
    internal static partial class SearchResultParser
    {
        private const string ItemStart = "<li class=\"block-row block-row--separated";

        // Profile posts and other content link elsewhere and are skipped. XenForo leaves out the slug of titles without URL-safe characters.
        [GeneratedRegex("<h3 class=\"contentRow-title\">\\s*<a href=\"(/forums/threads/(?:[^\"/]*?\\.)?(\\d+)/)(?:post-(\\d+))?\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
        private static partial Regex TitleRegex();

        [GeneratedRegex("<span class=\"label[^\"]*\"[^>]*>(.*?)</span>", RegexOptions.Singleline)]
        private static partial Regex PrefixRegex();

        [GeneratedRegex("<li>Post #([\\d,.]+)</li>")]
        private static partial Regex PositionRegex();

        [GeneratedRegex("<time\\s[^>]*data-timestamp=\"(\\d+)\"")]
        private static partial Regex TimestampRegex();

        // Forums without node ID in their URL (".../forums/gen1/") too
        [GeneratedRegex("Forum: <a href=\"(/forums/forums/[^\"]+/)\"")]
        private static partial Regex ForumRegex();

        [GeneratedRegex("<a href=\"([^\"]+)\" class=\"pageNav-jump pageNav-jump--next\"")]
        private static partial Regex NextPageRegex();

        public static IList<SearchHit> Parse(string html, out string? nextPageUrl)
        {
            var next = NextPageRegex().Match(html);
            nextPageUrl = next.Success ? Common.SMOGON_URL + WebUtility.HtmlDecode(next.Groups[1].Value) : null;

            var hits = new List<SearchHit>();
            var start = html.IndexOf(ItemStart);
            while (start != -1)
            {
                var end = html.IndexOf(ItemStart, start + ItemStart.Length);
                var hit = ParseItem(end == -1 ? html[start..] : html[start..end]);
                if (hit != null)
                {
                    hits.Add(hit);
                }
                start = end;
            }
            return hits;
        }

        private static SearchHit? ParseItem(string block)
        {
            var title = TitleRegex().Match(block);
            var timestamp = TimestampRegex().Match(block);
            var forum = ForumRegex().Match(block);
            if (!title.Success || !timestamp.Success || !forum.Success)
            {
                return null;
            }

            long? postId = title.Groups[3].Success ? long.Parse(title.Groups[3].Value) : null;
            var position = 1;
            var positionMatch = PositionRegex().Match(block);
            if (postId != null && positionMatch.Success)
            {
                position = int.Parse(positionMatch.Groups[1].Value.Replace(",", "").Replace(".", ""), CultureInfo.InvariantCulture);
            }
            var prefix = PrefixRegex().Match(title.Groups[4].Value);

            return new SearchHit(
                ThreadId: long.Parse(title.Groups[2].Value),
                ThreadUrl: Common.SMOGON_URL + title.Groups[1].Value,
                PostId: postId,
                Position: position,
                PostDate: DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp.Groups[1].Value)).UtcDateTime,
                ForumUrl: Common.SMOGON_URL + WebUtility.HtmlDecode(forum.Groups[1].Value),
                Prefix: prefix.Success ? WebUtility.HtmlDecode(prefix.Groups[1].Value).Trim() : "");
        }
    }
}
