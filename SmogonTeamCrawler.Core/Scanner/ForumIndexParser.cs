using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Scanner
{
    /// <summary>
    /// Finds the sub-forums of a section on a XenForo forum index, category or forum page. Forums are told from link forums
    /// and categories by their path: Smogon's markup stopped marking sub-forum links with "subNodeLink--forum".
    /// </summary>
    internal static partial class ForumIndexParser
    {
        private const string ForumPath = "/forums/forums/";

        // "nodeTitle": the anchor opens the node's title (since 24 November 2026 it may be wrapped in an element-tooltip).
        [GeneratedRegex("(?<nodeTitle><h3 class=\"node-title\">\\s*(?:<element-tooltip\\b[^>]*>\\s*)?)?<a href=\"(?<href>[^\"]+)\"(?<attrs>[^>]*)>(?<inner>.*?)</a>", RegexOptions.Singleline)]
        private static partial Regex AnchorRegex();

        [GeneratedRegex("<ol\\b|</ol>|<a href=\"(?<href>/forums/forums/[^\"]+)\" class=\"subNodeLink[^\"]*\">(?<inner>.*?)</a>", RegexOptions.Singleline)]
        private static partial Regex SubNodeTokenRegex();

        [GeneratedRegex("<[^>]+>")]
        private static partial Regex TagRegex();

        /// <summary>
        /// Returns all sub-forums (at any depth) of the forum named <paramref name="section"/>, which can be a forum node
        /// of the page or a sub-forum link itself. Link forums are skipped. Returns null if the section does not exist.
        /// </summary>
        public static IList<(string Name, string Url)>? FindSubForums(string html, string section)
        {
            foreach (Match anchor in AnchorRegex().Matches(html))
            {
                var (isNode, isSubForum) = Kind(anchor);
                if ((!isNode && !isSubForum) || CleanName(anchor.Groups["inner"].Value) != section)
                {
                    continue;
                }

                var afterAnchor = anchor.Index + anchor.Length;
                var listStart = html.IndexOf("<ol", afterAnchor);
                // A node's sub-forum list ends before the next node; a sub-forum's child list comes before its </li>.
                var boundary = html.IndexOf(isNode ? "<div class=\"node node--" : "</li>", afterAnchor);
                if (listStart == -1 || (boundary != -1 && boundary < listStart))
                {
                    return [];
                }
                return CollectList(html, listStart);
            }
            return null;
        }

        /// <summary>
        /// All forums listed on the page of a forum: its sub-forums and their sub-forums, as far as the page shows them.
        /// Link forums are skipped.
        /// </summary>
        public static IList<(string Name, string Url)> FindForums(string html)
        {
            var forums = new List<(string Name, string Url)>();
            foreach (Match anchor in AnchorRegex().Matches(html))
            {
                var (isNode, isSubForum) = Kind(anchor);
                var href = anchor.Groups["href"].Value;
                if ((isNode || isSubForum) && href.StartsWith(ForumPath) && !forums.Any(forum => forum.Url == Common.SMOGON_URL + href))
                {
                    forums.Add((CleanName(anchor.Groups["inner"].Value), Common.SMOGON_URL + href));
                }
            }
            return forums;
        }

        /// <summary>
        /// A node title (forum or category) or a sub-forum link; link forums and sub-categories are neither. Node titles
        /// carried data-shortcut="node-description" until 24 November 2026; since then they are the link in the node's h3.
        /// </summary>
        private static (bool IsNode, bool IsSubForum) Kind(Match anchor)
        {
            var attrs = anchor.Groups["attrs"].Value;
            return (attrs.Contains("data-shortcut=\"node-description\"") || anchor.Groups["nodeTitle"].Success,
                attrs.Contains("class=\"subNodeLink") && anchor.Groups["href"].Value.StartsWith(ForumPath));
        }

        private static List<(string Name, string Url)> CollectList(string html, int listStart)
        {
            var subForums = new List<(string Name, string Url)>();
            var depth = 0;
            for (var token = SubNodeTokenRegex().Match(html, listStart); token.Success; token = token.NextMatch())
            {
                if (token.Value.StartsWith("<ol"))
                {
                    depth++;
                }
                else if (token.Value == "</ol>")
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
                else
                {
                    subForums.Add((CleanName(token.Groups["inner"].Value), Common.SMOGON_URL + token.Groups["href"].Value));
                }
            }
            return subForums;
        }

        private static string CleanName(string inner) => WebUtility.HtmlDecode(TagRegex().Replace(inner, "")).Trim();
    }
}
