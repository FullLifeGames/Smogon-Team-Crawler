#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SmogonTeamCrawler.Tests.Fakes
{
    internal record FakePost(long Id, DateTime Date, string? TeamTag = null, string? PasteUrl = null, string Author = "Tester");

    internal class FakeThread
    {
        public FakeThread(long id)
        {
            Id = id;
        }

        public long Id { get; }
        public string Url => SmogonHtml.ThreadUrl(Id);
        public bool Sticky { get; set; }
        public string Prefix { get; set; } = "";
        public List<List<FakePost>> Pages { get; } = new();
        public DateTime LatestPost => Pages.SelectMany(page => page).Max(post => post.Date);

        public FakeThread AddPage(params FakePost[] posts)
        {
            Pages.Add(posts.ToList());
            return this;
        }
    }

    /// <summary>A forum with threads that renders listing and thread pages the way Smogon (XenForo 2.3) does.</summary>
    internal class FakeForum
    {
        public FakeForum(string url)
        {
            Url = url;
        }

        public string Url { get; }
        public int ThreadsPerPage { get; set; } = 40;
        public List<FakeThread> Threads { get; } = new();

        public FakeThread AddThread(long id, bool sticky = false, string prefix = "")
        {
            var thread = new FakeThread(id) { Sticky = sticky, Prefix = prefix };
            Threads.Add(thread);
            return thread;
        }

        /// <summary>Registers all listing pages, thread pages and pastes of this forum.</summary>
        public void Publish(FakeHttpHandler http)
        {
            var path = new Uri(Url).AbsolutePath;
            var stickies = Threads.Where(t => t.Sticky).OrderByDescending(t => t.LatestPost).ToList();
            var normal = Threads.Where(t => !t.Sticky).OrderByDescending(t => t.LatestPost).ToList();
            var pages = Math.Max(1, (int)Math.Ceiling(normal.Count / (double)ThreadsPerPage));
            for (var page = 1; page <= pages; page++)
            {
                var items = (page == 1 ? stickies : new List<FakeThread>())
                    .Concat(normal.Skip((page - 1) * ThreadsPerPage).Take(ThreadsPerPage));
                http.Html(SmogonHtml.PageUrl(Url, page), SmogonHtml.Listing(path, page, pages, items));
            }

            foreach (var thread in Threads)
            {
                for (var page = 1; page <= thread.Pages.Count; page++)
                {
                    http.Html(SmogonHtml.PageUrl(thread.Url, page), SmogonHtml.ThreadPage(thread, page));
                    foreach (var post in thread.Pages[page - 1].Where(p => p.PasteUrl != null))
                    {
                        http.Html(post.PasteUrl!, SmogonHtml.Pokepaste(post.TeamTag ?? "paste"));
                    }
                }
            }
        }
    }

    internal static class SmogonHtml
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        public const string BotCheck = """
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><meta name="robots" content="noindex"><title>Smogon Forums</title></head>
            <body><p id="m">Checking your browser...</p>
            <noscript>JavaScript and cookies are required to view this page.</noscript>
            <script>document.cookie = "xf_smogon_gate=" + 1;</script></body></html>
            """;

        public static string ThreadUrl(long id) => $"https://www.smogon.com/forums/threads/thread-{id}.{id}/";

        public static string PageUrl(string baseUrl, int page) => page <= 1 ? baseUrl : baseUrl + "page-" + page;

        public static string DisplayDate(DateTime date) => date.ToString("MMM d, yyyy", English) + " at " + date.ToString("h:mm tt", English);

        public static long Timestamp(DateTime date) => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeSeconds();

        private static string Time(string cssClass, DateTime date) =>
            $"<time  class=\"{cssClass}u-dt\" dir=\"auto\" datetime=\"{date:yyyy-MM-ddTHH:mm:ss}Z\" data-timestamp=\"{Timestamp(date)}\" data-date=\"{date.ToString("MMM d, yyyy", English)}\" data-time=\"{date.ToString("h:mm tt", English)}\" data-short=\"1d\" title=\"{DisplayDate(date)}\">{DisplayDate(date)}</time>";

        private static string PageNav(string path, int pages)
        {
            if (pages <= 1)
            {
                return "";
            }
            var nav = new StringBuilder();
            nav.Append("<div class=\"block-outer\"><div class=\"block-outer-main\"><nav class=\"pageNavWrapper pageNavWrapper--mixed \">\n");
            nav.Append("<div class=\"pageNav  \">\n\t<ul class=\"pageNav-main\">\n");
            for (var page = 1; page <= pages; page++)
            {
                var css = page == 1 ? "pageNav-page pageNav-page--current " : "pageNav-page ";
                nav.Append($"\t\t<li class=\"{css}\"><a href=\"{PageUrl(path, page)}\">{page}</a></li>\n");
            }
            nav.Append("\t</ul>\n</div>\n</nav></div></div>\n");
            return nav.ToString();
        }

        public static string Listing(string forumPath, int page, int pages, IEnumerable<FakeThread> threads)
        {
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\n<html id=\"XF\" lang=\"en-US\" dir=\"LTR\" data-xf=\"2.3\" data-app=\"public\" data-template=\"forum_view\" data-logged-in=\"false\">\n<body>\n");
            html.Append(PageNav(forumPath, pages));
            html.Append("<div class=\"structItemContainer\">\n");
            foreach (var thread in threads)
            {
                var threadPath = new Uri(thread.Url).AbsolutePath;
                html.Append($"\t<div class=\"structItem structItem--thread js-inlineModContainer js-threadListItem-{thread.Id}\" data-author=\"Tester\">\n");
                html.Append("\t\t<div class=\"structItem-cell structItem-cell--main\" data-xf-init=\"touch-proxy\">\n");
                if (thread.Sticky)
                {
                    html.Append("\t\t\t<ul class=\"structItem-statuses\">\n\t\t\t\t<li>\n\t\t\t\t\t<i class=\"structItem-status structItem-status--sticky\" aria-hidden=\"true\" title=\"Sticky\"></i>\n\t\t\t\t\t<span class=\"u-srOnly\">Sticky</span>\n\t\t\t\t</li>\n\t\t\t</ul>\n");
                }
                html.Append("\t\t\t<div class=\"structItem-title\">\n");
                if (thread.Prefix.Length > 0)
                {
                    html.Append($"\t\t\t\t<a href=\"{forumPath}?prefix_id=1\" class=\"labelLink\" rel=\"nofollow\"><span class=\"label label--blue\" dir=\"auto\">{thread.Prefix}</span></a>\n");
                }
                html.Append($"\t\t\t\t<a href=\"{threadPath}\" class=\"\" data-tp-primary=\"on\" data-xf-init=\"preview-tooltip\" data-preview-url=\"{threadPath}preview\"\n\t\t\t\t\t>\n\t\t\t\t\tThread {thread.Id}\n\t\t\t\t</a>\n");
                html.Append("\t\t\t</div>\n\t\t\t<div class=\"structItem-minor\">\n");
                if (thread.Pages.Count > 1)
                {
                    html.Append("\t\t\t\t<span class=\"structItem-pageJump\">\n");
                    for (var p = Math.Max(2, thread.Pages.Count - 2); p <= thread.Pages.Count; p++)
                    {
                        html.Append($"\t\t\t\t\t<a href=\"{threadPath}page-{p}\">{p}</a>\n");
                    }
                    html.Append("\t\t\t\t</span>\n");
                }
                html.Append("\t\t\t</div>\n\t\t</div>\n");
                html.Append("\t\t<div class=\"structItem-cell structItem-cell--latest\">\n");
                html.Append($"\t\t\t<a href=\"{threadPath}latest\" rel=\"nofollow\">{Time("structItem-latestDate ", thread.LatestPost)}</a>\n");
                html.Append("\t\t</div>\n\t</div>\n");
            }
            html.Append("</div>\n</body>\n</html>\n");
            return html.ToString();
        }

        public static string ThreadPage(FakeThread thread, int page)
        {
            var threadPath = new Uri(thread.Url).AbsolutePath;
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\n<html id=\"XF\" lang=\"en-US\" dir=\"LTR\" data-xf=\"2.3\" data-app=\"public\" data-template=\"thread_view\" data-logged-in=\"false\">\n<body>\n");
            html.Append(PageNav(threadPath, thread.Pages.Count));
            html.Append("<div class=\"block-body js-replyNewMessageContainer\">\n");
            foreach (var post in thread.Pages[page - 1])
            {
                html.Append("<article class=\"message message--post js-post js-inlineModContainer  \"\n");
                html.Append($"\t\tdata-author=\"{post.Author}\"\n");
                html.Append($"\t\tdata-content=\"post-{post.Id}\"\n");
                html.Append($"\t\tid=\"js-post-{post.Id}\">\n");
                html.Append("\t<header class=\"message-attribution message-attribution--split\">\n");
                html.Append("\t\t<ul class=\"message-attribution-main listInline \">\n\t\t\t<li class=\"u-concealed\">\n");
                html.Append($"\t\t\t\t<a href=\"{threadPath}post-{post.Id}\" rel=\"nofollow\" >\n");
                html.Append($"\t\t\t\t\t{Time("", post.Date)}\n");
                html.Append("\t\t\t\t</a>\n\t\t\t</li>\n\t\t</ul>\n\t</header>\n");
                html.Append("\t<div class=\"message-content js-messageContent\">\n\t\t<article class=\"message-body js-selectToQuote\">\n");
                html.Append("<div class=\"bbWrapper\">Post text<br />\n");
                if (post.TeamTag != null && post.PasteUrl == null)
                {
                    html.Append("<div class=\"bbCodeBlock bbCodeBlock--spoiler\">\n<div class=\"bbCodeBlock-content\">\n");
                    html.Append(TeamText(post.TeamTag, "<br />"));
                    html.Append("</div>\n</div>\n");
                }
                if (post.PasteUrl != null)
                {
                    html.Append($"<a href=\"{post.PasteUrl}\" target=\"_blank\" class=\"link link--external\" rel=\"nofollow ugc noopener\">{post.PasteUrl}</a>\n");
                }
                html.Append("</div>\n\t\t</article>\n\t</div>\n");
                html.Append("\t</article>\n");
            }
            html.Append("</div>\n</body>\n</html>\n");
            return html.ToString();
        }

        public static string Pokepaste(string tag)
        {
            var html = new StringBuilder("<html><body>\n<article>\n");
            foreach (var mon in TeamText(tag, "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                html.Append("<pre>\n").Append(mon.Trim('\n')).Append('\n').Append("</pre>\n");
            }
            html.Append("</article>\n</body></html>\n");
            return html.ToString();
        }

        /// <summary>A six Pokémon team in Showdown format; the first mon is named after the tag.</summary>
        public static string TeamText(string tag, string lineEnd)
        {
            var team = new StringBuilder();
            for (var i = 1; i <= 6; i++)
            {
                team.Append($"Mon{tag}x{i} @ Leftovers{lineEnd}\n");
                team.Append($"Ability: Pressure{lineEnd}\n");
                team.Append($"EVs: 252 HP / 252 Def / 4 SpD{lineEnd}\n");
                team.Append($"Bold Nature{lineEnd}\n");
                team.Append($"- Protect{lineEnd}\n- Toxic{lineEnd}\n- Roost{lineEnd}\n- Scald{lineEnd}\n");
                team.Append($"{lineEnd}\n");
            }
            return team.ToString();
        }
    }
}
