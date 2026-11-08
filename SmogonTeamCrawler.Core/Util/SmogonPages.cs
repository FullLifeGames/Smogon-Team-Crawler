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
            html.Contains("xf_smogon_gate") || (html.Contains("Checking your browser") && !html.Contains("data-template=\""));

        /// <summary>Forum index, category and forum listing pages carry the XenForo template name on the html tag.</summary>
        public static void EnsureTemplate(string html, string url, string template)
        {
            if (IsBotCheck(html))
            {
                throw new SmogonAccessBlockedException(url);
            }
            if (!html.Contains("data-template=\"" + template + "\""))
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
            // message--post, or message--article for the first post of article threads
            if (!html.Contains("<article class=\"message message--"))
            {
                throw new UnexpectedPageException(url, "the page contains no posts");
            }
        }

        [GeneratedRegex("class=\"pageNav-page[^\"]*\"[^>]*>\\s*<a[^>]*>\\s*(\\d+)\\s*</a>")]
        private static partial Regex PageNavPageRegex();

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
