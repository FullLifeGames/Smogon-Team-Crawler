using System;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Util
{
    /// <summary>
    /// Smogon answered with a bot check or block page instead of the requested content.
    /// The crawl stops so that the cached state is not overwritten with empty results.
    /// </summary>
    public class SmogonAccessBlockedException : Exception
    {
        public string Url { get; }

        public SmogonAccessBlockedException(string url)
            : base($"Smogon returned a bot check instead of the page {url}. The crawl was stopped and the cache was left untouched.")
        {
            Url = url;
        }
    }

    /// <summary>
    /// A page did not look like the expected forum page (changed markup, error page, ...).
    /// The crawl stops so that the cached state is not overwritten with empty results.
    /// </summary>
    public class UnexpectedPageException : Exception
    {
        public string Url { get; }

        public UnexpectedPageException(string url, string reason)
            : base($"Unexpected content at {url}: {reason}. The crawl was stopped and the cache was left untouched.")
        {
            Url = url;
        }
    }

    internal static partial class SmogonPages
    {
        /// <summary>Page 1 is requested without suffix, since ".../page-1" answers with a redirect (one extra request).</summary>
        public static string PageUrl(string baseUrl, int page) => page <= 1 ? baseUrl : baseUrl + "page-" + page;

        public static bool IsBotCheck(string html) =>
            html.Contains("xf_smogon_gate") || ((html.Contains("forum_gate=") || html.Contains("Checking your browser")) && !HasTemplate(html));

        /// <summary>
        /// Whether the html tag names the XenForo template (any if null): data-template="forum_list" until 2026-11-23,
        /// the class template-forum_list since 2026-11-24.
        /// </summary>
        public static bool HasTemplate(string html, string? template = null)
        {
            var name = template == null ? "[^\"\\s]+" : Regex.Escape(template);
            return Regex.IsMatch(html, "<html\\b[^>]*?\\s(?:data-template=\"" + name + "\"|class=\"(?:[^\"]*\\s)?template-" + name + "[\"\\s])");
        }

        /// <summary>Forum index, category and forum listing pages carry the XenForo template name on the html tag.</summary>
        public static void EnsureTemplate(string html, string url, string template)
        {
            if (IsBotCheck(html))
            {
                throw new SmogonAccessBlockedException(url);
            }
            if (!HasTemplate(html, template))
            {
                throw new UnexpectedPageException(url, "expected the XenForo template '" + template + "'");
            }
        }

        public static void EnsureThreadPage(string html, string url)
        {
            if (IsBotCheck(html))
            {
                throw new SmogonAccessBlockedException(url);
            }
            var posts = PostStartRegex().Matches(html);
            if (posts.Count == 0)
            {
                throw new UnexpectedPageException(url, "the page contains no posts");
            }
            // A page the team parser cannot read in full is skipped and its stored teams are kept, not stored half read.
            for (var i = 0; i < posts.Count; i++)
            {
                var next = i + 1 < posts.Count ? posts[i + 1].Index : html.Length;
                var end = html.IndexOf("\n\t</article>", posts[i].Index, next - posts[i].Index, StringComparison.Ordinal);
                var post = end < 0 ? null : html[posts[i].Index..end];
                var missing = post == null ? "its end (a line \"\\t</article>\")"
                    : !post.Contains("data-author=\"", StringComparison.Ordinal) ? "data-author"
                    : !PostIdRegex().IsMatch(post) ? "id=\"js-post-N\""
                    : !PostDateRegex().IsMatch(post) ? "a date in its message-attribution--split header"
                    : !post.Contains("<div class=\"bbWrapper\">", StringComparison.Ordinal) ? "its text (bbWrapper)"
                    : null;
                if (missing != null)
                {
                    throw new UnexpectedPageException(url, $"post {i + 1} of {posts.Count} has no {missing}");
                }
            }
        }

        [GeneratedRegex("id=\"js-post-\\d+\"")]
        private static partial Regex PostIdRegex();

        // TeamCollector reads the date from the title, "Mar 22, 2021 at 12:15 AM"
        [GeneratedRegex("<header class=\"message-attribution message-attribution--split\">[\\s\\S]*?\\btitle=\"[A-Z][a-z]{2} \\d{1,2}, \\d{4} at \\d{1,2}:\\d{2} [AP]M\"")]
        private static partial Regex PostDateRegex();

        [GeneratedRegex("<article class=\"message [^\"]*\\bjs-post\\b")]
        private static partial Regex PostStartRegex();

        /// <summary>
        /// Whether the line opens a post: "message js-post …" since Smogon's markup change of November 2026, before that
        /// "message message--post js-post …" (or message--article for the first post of article threads).
        /// </summary>
        public static bool IsPostStart(string line) => line.Contains("<article class=\"message ") && PostStartRegex().IsMatch(line);

        [GeneratedRegex("class=\"pageNav-page[^\"]*\"[^>]*>\\s*<a[^>]*>\\s*(\\d+)\\s*</a>")]
        private static partial Regex PageNavPageRegex();

        [GeneratedRegex("class=\"pageNav-page pageNav-page--current[^\"]*\"[^>]*>\\s*<a[^>]*>\\s*(\\d+)\\s*</a>")]
        private static partial Regex CurrentPageRegex();

        /// <summary>The page shown, from the page navigation (a post link like ".../post-123" lands on the page of the post).</summary>
        public static int GetCurrentPage(string html)
        {
            var current = CurrentPageRegex().Match(html);
            return current.Success && int.TryParse(current.Groups[1].Value, out var page) ? page : 1;
        }

        [GeneratedRegex("\\btimestamp=\"(\\d+)\"")]
        private static partial Regex TimestampRegex();

        /// <summary>
        /// The Unix time of a &lt;time&gt; tag, given with the tag that follows it: data-timestamp="N" on the tag until
        /// 2026-11-25, since 2026-11-26 timestamp="N" on the &lt;dynamic-time&gt; inside it. Null if it has none.
        /// </summary>
        public static long? GetTimestamp(string timeTag)
        {
            var timestamp = TimestampRegex().Match(timeTag);
            return timestamp.Success ? long.Parse(timestamp.Groups[1].Value) : null;
        }

        [GeneratedRegex("\\.(\\d+)/?$")]
        private static partial Regex NodeIdRegex();

        /// <summary>The node ID of a forum URL (".../forums/overused.726/" is 726), null if there is none (".../forums/gen1/").</summary>
        public static long? GetForumId(string forumUrl)
        {
            var id = NodeIdRegex().Match(forumUrl);
            return id.Success ? long.Parse(id.Groups[1].Value) : null;
        }

        [GeneratedRegex("data-container-key=\"node-(\\d+)\"")]
        private static partial Regex ContainerKeyRegex();

        // Since 2026-11-24 only the quick search's "This forum" option names the node: {"search_type":"post","c":{"nodes":[525],...}}
        [GeneratedRegex("&quot;c&quot;:\\{&quot;nodes&quot;:\\[(\\d+)\\]")]
        private static partial Regex SearchNodeRegex();

        /// <summary>The node ID of the forum a listing page belongs to, from its html tag or quick search; null if it has none.</summary>
        public static long? GetNodeId(string listingHtml)
        {
            var id = ContainerKeyRegex().Match(listingHtml);
            id = id.Success ? id : SearchNodeRegex().Match(listingHtml);
            return id.Success ? long.Parse(id.Groups[1].Value) : null;
        }

        public static int GetNumberOfPages(string html)
        {
            var pages = 1;
            if (!html.Contains("<nav class=\"pageNavWrapper"))
            {
                return pages;
            }
            foreach (Match match in PageNavPageRegex().Matches(html))
            {
                if (int.TryParse(match.Groups[1].Value, out var page) && page > pages)
                {
                    pages = page;
                }
            }
            return pages;
        }
    }
}
