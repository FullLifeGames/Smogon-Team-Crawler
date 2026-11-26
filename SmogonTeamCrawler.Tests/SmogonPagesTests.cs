#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class SmogonPagesTests
    {
        [TestCase("<html id=\"XF\" data-app=\"public\" data-template=\"forum_view\">", true)] // until 2026-11-23
        [TestCase("<html id=\"forum\" lang=\"en-US\"\n\tdata-app=\"public\"\n\tclass=\"template-forum_view\">", true)] // since 2026-11-24
        [TestCase("<html class=\"has-js template-forum_view is-wide\">", true)]
        [TestCase("<html class=\"template-forum_view_x\">", false)]
        [TestCase("<html class=\"template-forum_list\">", false)]
        [TestCase("<html lang=\"en-US\"><body class=\"template-forum_view\">", false)]
        [TestCase("<html lang=\"en-US\">", false)]
        public void HasTemplate(string html, bool expected)
        {
            Assert.That(SmogonPages.HasTemplate(html, "forum_view"), Is.EqualTo(expected));
        }

        [TestCase("<html id=\"XF\" data-template=\"forum_view\"\n\tdata-container-key=\"node-726\">")] // until 2026-11-23
        [TestCase("<option value=\"{&quot;search_type&quot;:&quot;post&quot;,&quot;c&quot;:{&quot;nodes&quot;:[726],&quot;child_nodes&quot;:1}}\">This forum</option>")] // since 2026-11-24
        public void GetNodeId(string html)
        {
            Assert.That(SmogonPages.GetNodeId(html), Is.EqualTo(726));
        }

        [TestCase("<h4 class=\"message-name\">")]
        [TestCase("Tester")] // the name in it
        [TestCase("id=\"js-post-")]
        [TestCase(" title=\"")]
        [TestCase("<div class=\"bbWrapper\">")]
        [TestCase("\n\t</article>")]
        public void EnsureThreadPage_WantsWhatTheParserReadsFromEveryPost(string marker)
        {
            const string url = "https://www.smogon.com/forums/threads/test.1/";
            var html = SmogonHtml.ThreadPage(new FakeForum("https://www.smogon.com/forums/forums/test.1/").AddThread(1)
                .AddPage(new FakePost(11, new System.DateTime(2026, 11, 24, 12, 0, 0)), new FakePost(12, new System.DateTime(2026, 11, 24, 13, 0, 0))), 1);
            SmogonPages.EnsureThreadPage(html, url);

            html = html.Remove(html.LastIndexOf(marker), marker.Length);

            Assert.That(() => SmogonPages.EnsureThreadPage(html, url), Throws.TypeOf<UnexpectedPageException>().With.Message.Contains("post 2 of 2"));
        }

        [Test]
        public void EnsureThreadPage_TakesTheAuthorFromDataAuthor_AsUntil2026_11_25_ButNotFromTheLineThatOpensThePost()
        {
            const string url = "https://www.smogon.com/forums/threads/test.1/";
            var html = SmogonHtml.ThreadPage(new FakeForum("https://www.smogon.com/forums/forums/test.1/").AddThread(1)
                .AddPage(new FakePost(11, new System.DateTime(2026, 11, 24, 12, 0, 0))), 1);
            html = Regex.Replace(html, "\t\t\t<h4 class=\"message-name\">.*</h4>\n", "");
            Assert.That(html, Does.Not.Contain("message-name"));

            SmogonPages.EnsureThreadPage(html.Replace(SmogonHtml.PostTag + "\n", SmogonHtml.PostTag + "\n\t\tdata-author=\"Tester\"\n"), url);

            // There the parser does not read it.
            Assert.That(() => SmogonPages.EnsureThreadPage(html.Replace(SmogonHtml.PostTag + "\n", SmogonHtml.PostTag + " data-author=\"Tester\"\n"), url),
                Throws.TypeOf<UnexpectedPageException>().With.Message.Contains("post 1 of 1 has no author"));
        }

        [Test]
        public void EnsureThreadPage_PassesThePageOf2026_11_26_ButNotWithoutTheAuthorLineOfAPost()
        {
            var url = SavedPages.ThreadPageUrl + "page-29";
            SmogonPages.EnsureThreadPage(SavedPages.ThreadPage, url);

            // Post 2 has no team, so the parser would not stop at it. The line end stays: git checks the fixture out with \r\n on Windows.
            var author = Regex.Matches(SavedPages.ThreadPage, "<h4 class=\"message-name\">.*?</h4>")[1];
            var html = SavedPages.ThreadPage.Remove(author.Index, author.Length);

            Assert.That(() => SmogonPages.EnsureThreadPage(html, url),
                Throws.TypeOf<UnexpectedPageException>().With.Message.Contains("post 2 of 21 has no author"));
        }

        [Test]
        public void IsBotCheck_KnowsTheGateCookieOf2026_11_24_ButNotAPostAboutIt()
        {
            Assert.That(SmogonPages.IsBotCheck(SmogonHtml.BotCheck.Replace("xf_smogon_gate", "forum_gate").Replace("Checking your browser...", "")), Is.True);
            Assert.That(SmogonPages.IsBotCheck("<html class=\"template-thread_view\"><p>Checking your browser... \"forum_gate=\"</p></html>"), Is.False);
        }
    }
}
