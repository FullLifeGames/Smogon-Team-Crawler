#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Scanner;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class TeamScannerTests
    {
        private static string SubForum(string path, string name, string children = "", bool link = false) =>
            link
                ? $"\t<li>\n\t\t<a href=\"/forums/link-forums/{path}/\" class=\"subNodeLink subNodeLink--link\">{name}</a>\n\t</li>\n"
                : $"\t<li>\n\t\t<a href=\"/forums/forums/{path}/\" class=\"subNodeLink subNodeLink--forum subNodeLink--unread\">\n"
                  + $"\t\t\t<i class=\"fa--xf far fa-comments  subNodeLink-icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-hidden=\"true\" ><use href=\"/forums/data/local/icons/regular.svg?v=1#comments\"></use></svg></i>{name}\n"
                  + "\t\t</a>\n"
                  + (children.Length > 0 ? "\t\t<ol>\n" + children + "\t\t</ol>\n" : "")
                  + "\t</li>\n";

        private static string Node(int id, string path, string name, string subForums = "") =>
            $"<div class=\"node node--id{id} node--depth2 node--forum node--unread\">\n"
            + "\t<div class=\"node-body\">\n\t\t<div class=\"node-main js-nodeMain\">\n\t\t\t<h3 class=\"node-title\">\n"
            + $"\t\t\t\t<a href=\"/forums/forums/{path}/\" data-xf-init=\"element-tooltip\" data-shortcut=\"node-description\">{name}</a>\n"
            + "\t\t\t</h3>\n"
            + (subForums.Length > 0
                ? "\t\t\t<div class=\"node-subNodesFlat\">\n\t\t\t\t<span class=\"node-subNodesLabel\">Sub-forums:</span>\n\t\t\t\t<ol class=\"node-subNodeFlatList\">\n" + subForums + "\t\t\t\t</ol>\n\t\t\t</div>\n"
                : "")
            + "\t\t</div>\n\t\t<div class=\"node-stats\">\n\t\t\t<div class=\"node-statsMeta\"><dl class=\"pairs pairs--rows\"><dt>Threads</dt><dd>1K</dd></dl></div>\n\t\t</div>\n"
            + "\t</div>\n</div>\n";

        private static string Page(string template, params string[] nodes)
        {
            var html = new StringBuilder($"<!DOCTYPE html>\n<html id=\"XF\" data-template=\"{template}\">\n<body>\n<div class=\"block-body\">\n");
            foreach (var node in nodes)
            {
                html.Append(node);
            }
            return html.Append("</div>\n</body>\n</html>\n").ToString();
        }

        private static readonly string Index = Page("forum_list",
            Node(383, "intro_hub", "Information &amp; Resources", SubForum("admin-reports.359", "Admin Reports")),
            Node(725, "smogon-metagames.725", "Smogon Metagames",
                SubForum("overused.726", "OverUsed",
                    SubForum("gen-9-ou-archive.841", "Gen 9 OU Archive") + SubForum("ou-analyses.683", "OU Analyses", link: true))
                + SubForum("ubers.727", "Ubers")),
            Node(31, "ruins-of-alph.31", "Ruins of Alph",
                SubForum("gen1", "RBY", SubForum("rby-archive.976", "RBY Archive")) + SubForum("rate-my-team-rby.777", "Rate My Team (RBY)", link: true)),
            Node(457, "cartridge-card-metagames.457", "Cartridge &amp; Card Metagames", SubForum("vgc.127", "VGC")),
            Node(52, "rate-my-team.52", "Rate My Team",
                SubForum("sv-ou-teams.748", "SV OU Teams") + SubForum("sv-other-teams.749", "SV Other Teams")),
            Node(53, "wi-fi.53", "Wi-Fi", SubForum("wi-fi-archive.1", "Wi-Fi Archive")),
            Node(283, "thread-cryonics.283", "Thread Cryonics",
                SubForum("gen-7-competitive-discussion.249", "Gen 7 Competitive Discussion", SubForum("sm-ou.387", "SM OU"))
                + SubForum("gen-8-competitive-discussion.525", "Gen 8 Competitive Discussion",
                    SubForum("ss-ou.526", "SS OU", SubForum("gen-8-ou-archive.637", "Gen 8 OU Archive")) + SubForum("ss-ubers.527", "SS Ubers"))
                + SubForum("the-smog.79", "The Smog")));

        private static readonly string Archive = Page("category_view",
            Node(322, "2006-2015-b101-archive.322", "2006 - 2015 B101 Archive", SubForum("2020-2023-b101-resources.557", "2020 - 2023 B101 Resources")),
            Node(165, "2007-2022-rate-my-team.165", "2007 - 2022 Rate My Team",
                SubForum("bw-ou-teams.119", "BW OU Teams") + SubForum("ss-other-teams.546", "SS Other Teams")),
            Node(187, "2011-2013-translation-projects.187", "2011 - 2013 Translation Projects"));

        private static async Task<SmogonTeamCrawler.Core.Data.ScanResult> Scan(string index, string archive)
        {
            var http = new FakeHttpHandler();
            http.Html(Common.SMOGON_FORUMS_URL, index);
            http.Html(Common.ARCHIVE_URL, archive);
            return await new TeamScanner(new HttpClient(http)).Scan();
        }

        [Test]
        public async Task Scan_FindsTheSubForumsOfTheConfiguredSectionsOnly()
        {
            var result = await Scan(Index, Archive);

            Assert.That(result.TierToRegularLinks, Is.EquivalentTo(new[]
            {
                Pair("gen9OverUsed", "overused.726"),
                Pair("gen9Gen 9 OU Archive", "gen-9-ou-archive.841"),
                Pair("gen9Ubers", "ubers.727"),
                Pair("gen8RBY", "gen1"),
                Pair("gen8RBY Archive", "rby-archive.976"),
                Pair("gen8SS OU", "ss-ou.526"),
                Pair("gen8Gen 8 OU Archive", "gen-8-ou-archive.637"),
                Pair("gen8SS Ubers", "ss-ubers.527"),
            }));
            Assert.That(result.TierToRmtLinks, Is.EquivalentTo(new[]
            {
                Pair("SV OU Teams", "sv-ou-teams.748"),
                Pair("SV Other Teams", "sv-other-teams.749"),
                Pair("BW OU Teams", "bw-ou-teams.119"),
                Pair("SS Other Teams", "ss-other-teams.546"),
            }));
        }

        [Test]
        public async Task Scan_WithAMissingSection_StillReturnsTheOthers()
        {
            var result = await Scan(Index.Replace(">Rate My Team<", ">Team Rating<"), Archive);

            Assert.That(result.TierToRmtLinks.Keys, Is.EquivalentTo(new[] { "BW OU Teams", "SS Other Teams" }));
            Assert.That(result.TierToRegularLinks, Has.Count.EqualTo(8));
        }

        [Test]
        public void Scan_WithABotCheck_StopsTheCrawl()
        {
            Assert.ThrowsAsync<SmogonAccessBlockedException>(() => Scan(SmogonHtml.BotCheck, Archive));
        }

        private static System.Collections.Generic.KeyValuePair<string, string> Pair(string key, string path) =>
            new(key, "https://www.smogon.com/forums/forums/" + path + "/");
    }

    [TestFixture]
    public class ForumListingParserTests
    {
        [Test]
        public void Parse_ReadsThreadMetadata()
        {
            var forum = new FakeForum("https://www.smogon.com/forums/forums/sv-ou-teams.748/");
            forum.AddThread(10, sticky: true).AddPage(new FakePost(1, new System.DateTime(2024, 7, 5, 0, 18, 0)));
            forum.AddThread(11, prefix: "SV &amp; OU")
                .AddPage(new FakePost(2, new System.DateTime(2026, 11, 8, 13, 33, 0)))
                .AddPage(new FakePost(3, new System.DateTime(2026, 11, 8, 13, 34, 0)))
                .AddPage(new FakePost(4, new System.DateTime(2026, 11, 8, 13, 35, 0)));
            var html = SmogonHtml.Listing("/forums/forums/sv-ou-teams.748/", 1, 45, forum.Threads, forum.NodeId)
                .Replace("/forums/threads/thread-10.10/", "/forums/threads/10/");

            var items = ForumListingParser.Parse(html);

            Assert.That(SmogonPages.GetNumberOfPages(html), Is.EqualTo(45));
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].ThreadId, Is.EqualTo(10));
            Assert.That(items[0].Url, Is.EqualTo("https://www.smogon.com/forums/threads/10/"));
            Assert.That(items[0].Sticky, Is.True);
            Assert.That(items[0].Prefix, Is.Empty);
            Assert.That(items[0].LastPage, Is.EqualTo(1));
            Assert.That(items[1].ThreadId, Is.EqualTo(11));
            Assert.That(items[1].Url, Is.EqualTo(SmogonHtml.ThreadUrl(11)));
            Assert.That(items[1].Sticky, Is.False);
            Assert.That(items[1].Prefix, Is.EqualTo("SV & OU"));
            Assert.That(items[1].LastPage, Is.EqualTo(3));
            Assert.That(items[1].LatestPostTimestamp, Is.EqualTo(SmogonHtml.Timestamp(new System.DateTime(2026, 11, 8, 13, 35, 0))));
            Assert.That(items[1].LatestPostDisplayDate, Is.EqualTo("Nov 8, 2026 at 1:35 PM"));
        }

        [Test]
        public void GetNumberOfPages_WithoutPageNav_IsOne()
        {
            Assert.That(SmogonPages.GetNumberOfPages("<html data-template=\"forum_view\"></html>"), Is.EqualTo(1));
        }

        [Test]
        public void PageUrl_LeavesOutPage1()
        {
            Assert.That(SmogonPages.PageUrl("https://www.smogon.com/forums/threads/x.1/", 1), Is.EqualTo("https://www.smogon.com/forums/threads/x.1/"));
            Assert.That(SmogonPages.PageUrl("https://www.smogon.com/forums/threads/x.1/", 2), Is.EqualTo("https://www.smogon.com/forums/threads/x.1/page-2"));
        }
    }
}
