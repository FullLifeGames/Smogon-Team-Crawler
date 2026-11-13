using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Scanner
{
    /// <summary>Finds the sub-forums of a section on a XenForo forum index or category page.</summary>
    internal static partial class ForumIndexParser
    {
        [GeneratedRegex("<a href=\"(?<href>[^\"]+)\"(?<attrs>[^>]*)>(?<inner>.*?)</a>", RegexOptions.Singleline)]
        private static partial Regex AnchorRegex();

        [GeneratedRegex("<ol\\b|</ol>|<a href=\"(?<href>[^\"]+)\" class=\"subNodeLink subNodeLink--forum[^\"]*\">(?<inner>.*?)</a>", RegexOptions.Singleline)]
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
                var attrs = anchor.Groups["attrs"].Value;
                var isNode = attrs.Contains("data-shortcut=\"node-description\"");
                var isSubForum = attrs.Contains("class=\"subNodeLink subNodeLink--forum");
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
