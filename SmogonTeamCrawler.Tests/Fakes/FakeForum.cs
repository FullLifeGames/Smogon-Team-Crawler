#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        /// <param name="nodeId">The node ID, for a URL without one (".../forums/gen1/").</param>
        public FakeForum(string url, long? nodeId = null)
        {
            Url = url;
            var dot = url.TrimEnd('/').LastIndexOf('.');
            NodeId = nodeId ?? (long.TryParse(url.TrimEnd('/')[(dot + 1)..], out var id) ? id : throw new ArgumentException("No node ID in " + url));
        }

        public string Url { get; }
        public long NodeId { get; }
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
                http.Html(SmogonHtml.PageUrl(Url, page), SmogonHtml.Listing(path, page, pages, items, NodeId));
            }

            foreach (var thread in Threads)
            {
                for (var page = 1; page <= thread.Pages.Count; page++)
                {
                    var html = SmogonHtml.ThreadPage(thread, page);
                    http.Html(SmogonHtml.PageUrl(thread.Url, page), html);
                    foreach (var post in thread.Pages[page - 1])
                    {
                        // A post link leads to the page of the post.
                        http.Html(thread.Url + "post-" + post.Id, html);
                        if (post.PasteUrl != null)
                        {
                            http.Html(post.PasteUrl, SmogonHtml.Pokepaste(post.TeamTag ?? "paste"));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Smogon's search over fake forums: a post is a hit if its text (paste link, team or "Post text") matches the query
    /// (<see cref="FakeQuery"/>) and it is in one of the forums asked for (all without). Dates are compared in UTC.
    /// Results are newest first, cut off after <see cref="MaxResults"/>.
    /// </summary>
    internal class FakeSearch
    {
        private readonly FakeForum[] _forums;

        public FakeSearch(params FakeForum[] forums)
        {
            _forums = forums;
        }

        public int ResultsPerPage { get; set; } = 10;
        public int MaxResults { get; set; } = 200;
        /// <summary>Served for every search instead of results.</summary>
        public string? Unavailable { get; set; }
        /// <summary>Posts the search does not find.</summary>
        public HashSet<long> NotIndexed { get; } = new();
        /// <summary>Positions shown instead of the real one, as after posts were deleted or restored.</summary>
        public Dictionary<long, int> ShownPositions { get; } = new();

        public void Publish(FakeHttpHandler http) => http.Prefix("https://www.smogon.com/forums/search/", Answer);

        private HttpResponseMessage Answer(string url)
        {
            if (Unavailable != null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Unavailable) };
            }
            var query = url[(url.IndexOf('?') + 1)..].Split('&')
                .Select(part => part.Split('=', 2))
                .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "");
            var keywords = query.GetValueOrDefault("q") ?? "";
            var newerThan = query.TryGetValue("c[newer_than]", out var since) ? DateTime.Parse(since, CultureInfo.InvariantCulture) : DateTime.MinValue;
            var olderThan = query.TryGetValue("c[older_than]", out var before) ? DateTime.Parse(before, CultureInfo.InvariantCulture) : DateTime.MaxValue;
            var page = query.TryGetValue("page", out var pageText) ? int.Parse(pageText) : 1;
            var nodes = query.Where(kv => kv.Key.StartsWith("c[nodes][")).Select(kv => long.Parse(kv.Value)).ToHashSet();

            var matches = FakeQuery.Parse(keywords);
            var results = (
                from forum in _forums
                where nodes.Count == 0 || nodes.Contains(forum.NodeId)
                from thread in forum.Threads
                from pageIndex in Enumerable.Range(0, thread.Pages.Count)
                from post in thread.Pages[pageIndex].Select((post, index) => (post, index))
                let text = post.post.PasteUrl ?? (post.post.TeamTag != null ? SmogonHtml.TeamText(post.post.TeamTag, "") : "Post text")
                where matches(text) && post.post.Date >= newerThan && post.post.Date < olderThan && !NotIndexed.Contains(post.post.Id)
                orderby post.post.Date descending
                select new FakeSearchResult(forum, thread, post.post,
                    ShownPositions.GetValueOrDefault(post.post.Id, pageIndex * 25 + post.index + 1),
                    pageIndex == 0 && post.index == 0)
            ).Take(MaxResults).ToList();

            var shown = results.Skip((page - 1) * ResultsPerPage).Take(ResultsPerPage);
            var next = page * ResultsPerPage < results.Count
                ? $"/forums/search/1/?page={page + 1}&amp;q={Uri.EscapeDataString(keywords)}"
                    + (newerThan != DateTime.MinValue ? $"&amp;c[newer_than]={newerThan:yyyy-MM-dd}" : "")
                    + (olderThan != DateTime.MaxValue ? $"&amp;c[older_than]={olderThan:yyyy-MM-dd}" : "")
                    + string.Concat(nodes.Select((node, index) => $"&amp;c[nodes][{index}]={node}")) + "&amp;o=date"
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SmogonHtml.SearchResults(shown, next)) };
        }
    }

    /// <summary>
    /// Smogon's query syntax, matched against a text: words (contained, ignoring case) are all required, "|" is or,
    /// quotes make a phrase, parentheses group.
    /// </summary>
    internal static class FakeQuery
    {
        public static Func<string, bool> Parse(string query)
        {
            var tokens = new List<string>();
            for (var i = 0; i < query.Length;)
            {
                var c = query[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c is '(' or ')' or '|')
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else if (c == '"')
                {
                    var end = query.IndexOf('"', i + 1);
                    tokens.Add(query[i..(end + 1)]);
                    i = end + 1;
                }
                else
                {
                    var end = i;
                    while (end < query.Length && !char.IsWhiteSpace(query[end]) && query[end] is not ('(' or ')' or '|' or '"'))
                    {
                        end++;
                    }
                    tokens.Add(query[i..end]);
                    i = end;
                }
            }
            var position = 0;
            var match = Or(tokens, ref position);
            if (position != tokens.Count)
            {
                throw new FormatException("Unbalanced query: " + query);
            }
            return match;
        }

        private static Func<string, bool> Or(List<string> tokens, ref int position)
        {
            var alternatives = new List<Func<string, bool>> { And(tokens, ref position) };
            while (position < tokens.Count && tokens[position] == "|")
            {
                position++;
                alternatives.Add(And(tokens, ref position));
            }
            return text => alternatives.Any(alternative => alternative(text));
        }

        private static Func<string, bool> And(List<string> tokens, ref int position)
        {
            var required = new List<Func<string, bool>>();
            while (position < tokens.Count && tokens[position] is not ("|" or ")"))
            {
                var token = tokens[position++];
                if (token == "(")
                {
                    required.Add(Or(tokens, ref position));
                    position++; // ")"
                }
                else
                {
                    var words = token.Trim('"');
                    required.Add(text => text.Contains(words, StringComparison.OrdinalIgnoreCase));
                }
            }
            return text => required.All(part => part(text));
        }
    }

    internal record FakeSearchResult(FakeForum Forum, FakeThread Thread, FakePost Post, int Position, bool IsThread);

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

        private static string PageNav(string path, int pages, int current = 1)
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
                var css = page == current ? "pageNav-page pageNav-page--current " : "pageNav-page ";
                nav.Append($"\t\t<li class=\"{css}\"><a href=\"{PageUrl(path, page)}\">{page}</a></li>\n");
            }
            nav.Append("\t</ul>\n</div>\n</nav></div></div>\n");
            return nav.ToString();
        }

        public static string Listing(string forumPath, int page, int pages, IEnumerable<FakeThread> threads, long nodeId)
        {
            var html = new StringBuilder();
            html.Append($"<!DOCTYPE html>\n<html id=\"XF\" lang=\"en-US\" dir=\"LTR\" data-xf=\"2.3\" data-app=\"public\" data-template=\"forum_view\"\n\tdata-container-key=\"node-{nodeId}\"\n\tdata-content-key=\"forum-{nodeId}\" data-logged-in=\"false\">\n<body>\n");
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
            html.Append(PageNav(threadPath, thread.Pages.Count, page));
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

        /// <summary>A page of search results as Smogon renders them; thread results are the first post of their thread.</summary>
        public static string SearchResults(IEnumerable<FakeSearchResult> results, string? nextHref)
        {
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\n<html id=\"XF\" lang=\"en-US\" dir=\"LTR\" data-xf=\"2.3\" data-app=\"public\" data-template=\"search_results\" data-logged-in=\"false\">\n<body>\n");
            html.Append("<div class=\"block-container\">\n\t<ol class=\"block-body\">\n");
            foreach (var result in results)
            {
                var threadPath = new Uri(result.Thread.Url).AbsolutePath;
                var link = result.IsThread ? threadPath : threadPath + "post-" + result.Post.Id;
                var label = result.Thread.Prefix.Length > 0
                    ? $"<span class=\"label label--blue\" dir=\"auto\">{result.Thread.Prefix}</span><span class=\"label-append\">&nbsp;</span>"
                    : "";
                html.Append($"\t\t<li class=\"block-row block-row--separated  js-inlineModContainer\" data-author=\"{result.Post.Author}\">\n");
                html.Append("\t<div class=\"contentRow \">\n\t\t<div class=\"contentRow-main\">\n");
                html.Append($"\t\t\t<h3 class=\"contentRow-title\">\n\t\t\t\t<a href=\"{link}\">{label}Thread {result.Thread.Id}</a>\n\t\t\t</h3>\n");
                html.Append($"\t\t\t<div class=\"contentRow-snippet\">{result.Post.PasteUrl ?? "Post text"}</div>\n");
                html.Append("\t\t\t<div class=\"contentRow-minor contentRow-minor--hideLinks\">\n\t\t\t\t<ul class=\"listInline listInline--bullet\">\n");
                html.Append($"\t\t\t\t\t<li><a href=\"/forums/members/tester.1/\" class=\"username \" dir=\"auto\" data-user-id=\"1\">{result.Post.Author}</a></li>\n");
                html.Append(result.IsThread ? "\t\t\t\t\t<li>Thread</li>\n" : $"\t\t\t\t\t<li>Post #{result.Position.ToString("N0", English)}</li>\n");
                html.Append($"\t\t\t\t\t<li>{Time("", result.Post.Date)}</li>\n");
                html.Append($"\t\t\t\t\t<li>Forum: <a href=\"{new Uri(result.Forum.Url).AbsolutePath}\">Forum</a></li>\n");
                html.Append("\t\t\t\t</ul>\n\t\t\t</div>\n\t\t</div>\n\t</div>\n</li>\n");
            }
            html.Append("\t</ol>\n</div>\n");
            if (nextHref != null)
            {
                html.Append($"<nav class=\"pageNavWrapper\"><div class=\"pageNav\"><a href=\"{nextHref}\" class=\"pageNav-jump pageNav-jump--next\">Next</a></div></nav>\n");
            }
            html.Append("</body>\n</html>\n");
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

        /// <summary>A six Pokémon team in Showdown format, with physical and special natures; the first mon is named after the tag.</summary>
        public static string TeamText(string tag, string lineEnd)
        {
            var team = new StringBuilder();
            for (var i = 1; i <= 6; i++)
            {
                team.Append($"Mon{tag}x{i} @ Leftovers{lineEnd}\n");
                team.Append($"Ability: Pressure{lineEnd}\n");
                team.Append($"EVs: 252 HP / 252 Def / 4 SpD{lineEnd}\n");
                team.Append(i % 2 == 0 ? $"Impish Nature{lineEnd}\n" : $"Bold Nature{lineEnd}\n");
                team.Append($"- Protect{lineEnd}\n- Toxic{lineEnd}\n- Roost{lineEnd}\n- Scald{lineEnd}\n");
                team.Append($"{lineEnd}\n");
            }
            return team.ToString();
        }
    }
}
