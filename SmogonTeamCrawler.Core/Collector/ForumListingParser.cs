using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>Reads the thread list of a forum listing page (XenForo "forum_view").</summary>
    internal static partial class ForumListingParser
    {
        private const string ItemStart = "<div class=\"structItem structItem--thread";

        // XenForo leaves out the slug (".../threads/123/") when a title has no URL-safe characters.
        [GeneratedRegex("data-preview-url=\"(/forums/threads/(?:[^\"/]*?\\.)?(\\d+)/)preview\"")]
        private static partial Regex PreviewUrlRegex();

        [GeneratedRegex("<div class=\"structItem-title\">(.*?)</div>", RegexOptions.Singleline)]
        private static partial Regex TitleRegex();

        [GeneratedRegex("<span class=\"label[^\"]*\"[^>]*>(.*?)</span>", RegexOptions.Singleline)]
        private static partial Regex PrefixRegex();

        [GeneratedRegex("<time\\s+class=\"structItem-latestDate[^\"]*\"([^>]*)>")]
        private static partial Regex LatestDateRegex();

        [GeneratedRegex("data-timestamp=\"(\\d+)\"")]
        private static partial Regex TimestampRegex();

        [GeneratedRegex("title=\"([^\"]*)\"")]
        private static partial Regex TitleAttributeRegex();

        [GeneratedRegex("<span class=\"structItem-pageJump\">(.*?)</span>", RegexOptions.Singleline)]
        private static partial Regex PageJumpRegex();

        [GeneratedRegex("/page-(\\d+)\"")]
        private static partial Regex PageLinkRegex();

        public static IList<ThreadListItem> Parse(string html)
        {
            var items = new List<ThreadListItem>();
            var start = html.IndexOf(ItemStart);
            while (start != -1)
            {
                var next = html.IndexOf(ItemStart, start + ItemStart.Length);
                var block = next == -1 ? html[start..] : html[start..next];
                var item = ParseItem(block);
                if (item != null)
                {
                    items.Add(item);
                }
                start = next;
            }
            return items;
        }

        private static ThreadListItem? ParseItem(string block)
        {
            // Moved-thread redirects and similar entries have no preview URL or latest post: skip them.
            var preview = PreviewUrlRegex().Match(block);
            var latest = LatestDateRegex().Match(block);
            if (!preview.Success || !latest.Success)
            {
                return null;
            }
            var timestamp = TimestampRegex().Match(latest.Groups[1].Value);
            if (!timestamp.Success)
            {
                return null;
            }

            var prefix = "";
            var title = TitleRegex().Match(block);
            if (title.Success)
            {
                var label = PrefixRegex().Match(title.Groups[1].Value);
                if (label.Success)
                {
                    prefix = WebUtility.HtmlDecode(label.Groups[1].Value).Trim();
                }
            }

            var lastPage = 1;
            var pageJump = PageJumpRegex().Match(block);
            if (pageJump.Success)
            {
                foreach (Match page in PageLinkRegex().Matches(pageJump.Groups[1].Value))
                {
                    if (int.TryParse(page.Groups[1].Value, out var number) && number > lastPage)
                    {
                        lastPage = number;
                    }
                }
            }

            var displayDate = TitleAttributeRegex().Match(latest.Groups[1].Value);

            return new ThreadListItem
            {
                ThreadId = long.Parse(preview.Groups[2].Value),
                Url = Common.SMOGON_URL + preview.Groups[1].Value,
                Prefix = prefix,
                Sticky = block.Contains("structItem-status--sticky"),
                LatestPostTimestamp = long.Parse(timestamp.Groups[1].Value),
                LatestPostDisplayDate = displayDate.Success ? WebUtility.HtmlDecode(displayDate.Groups[1].Value) : "",
                LastPage = lastPage,
            };
        }
    }
}
