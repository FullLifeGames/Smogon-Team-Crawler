#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;

namespace SmogonTeamCrawler.Tests
{
    /// <summary>The page guards, for Smogon's markup until 24 November 2026 (XenForo 2.3) and since (Amphoro).</summary>
    [TestFixture]
    public class SmogonPagesTests
    {
        private const string Url = "https://www.smogon.com/forums/";

        private const string XenForoIndex =
            "<!DOCTYPE html>\n<html id=\"XF\" lang=\"en-US\" dir=\"LTR\" data-xf=\"2.3\" data-app=\"public\" data-template=\"forum_list\" data-logged-in=\"false\">\n"
            + "<head>\n\t<meta charset=\"utf-8\" />\n</head>\n<body>\n<p>Forums</p>\n</body>\n</html>\n";

        /// <summary>The start of Smogon's forum index on 24 November 2026: the html tag spans several lines.</summary>
        private const string AmphoroIndex =
            "<!DOCTYPE html>\n<html id=\"forum\" lang=\"en-US\"\n\tdata-app=\"public\"\n\t\n\tdata-cookie-prefix=\"forum_\"\n\tclass=\"template-forum_list\">\n"
            + "<head>\n\t\n\n\t<meta charset=\"utf-8\" />\n\t<title>Smogon Forums</title>\n</head>\n<body>\n<p>Forums</p>\n</body>\n</html>\n";

        [TestCase(XenForoIndex)]
        [TestCase(AmphoroIndex)]
        public void HasTemplate_ReadsEitherMarkup(string html)
        {
            Assert.That(SmogonPages.GetTemplates(html), Is.EqualTo(new[] { "forum_list" }));
            Assert.That(SmogonPages.HasTemplate(html, "forum_list"), Is.True);
            Assert.That(SmogonPages.HasTemplate(html, "forum_view"), Is.False);
        }

        [TestCase("<html class=\"template-forum_view\">", true)]
        [TestCase("<html id=\"forum\" class=\"has-js template-forum_view is-wide\">", true)]
        [TestCase("<html\n\tclass=\"\ttemplate-forum_view\n\">", true)]
        [TestCase("<html data-template=\"forum_view\" class=\"has-js\">", true)]
        [TestCase("<html class=\"template-forum_view_x\">", false)]
        [TestCase("<html class=\"template-forum_vie\">", false)]
        [TestCase("<html class=\"x-template-forum_view\">", false)]
        [TestCase("<html class=\"template-\">", false)]
        [TestCase("<html data-template=\"forum_view_x\">", false)]
        [TestCase("<html data-class=\"template-forum_view\">", false)]
        [TestCase("<html data-x-data-template=\"forum_view\">", false)]
        [TestCase("<html lang=\"en-US\">\n<body class=\"template-forum_view\" data-template=\"forum_view\">", false)]
        [TestCase("<div class=\"template-forum_view\" data-template=\"forum_view\"></div>", false)]
        [TestCase("", false)]
        public void HasTemplate_LooksOnlyAtTheHtmlTag_AndMatchesTheClassAsAWholeWord(string html, bool expected)
        {
            Assert.That(SmogonPages.HasTemplate(html, "forum_view"), Is.EqualTo(expected));
        }

        [TestCase(XenForoIndex)]
        [TestCase(AmphoroIndex)]
        public void EnsureTemplate_AcceptsEitherMarkup(string html)
        {
            Assert.DoesNotThrow(() => SmogonPages.EnsureTemplate(html, Url, "forum_list"));
        }

        [Test]
        public void EnsureTemplate_WithNeitherMarker_StopsTheCrawl()
        {
            var html = AmphoroIndex.Replace("\n\tclass=\"template-forum_list\"", "");

            var error = Assert.Throws<UnexpectedPageException>(() => SmogonPages.EnsureTemplate(html, Url, "forum_list"));
            Assert.That(error!.Message, Is.EqualTo(
                "Unexpected content at https://www.smogon.com/forums/: expected the XenForo template 'forum_list', the page names none. The crawl was stopped and the cache was left untouched."));
        }

        [TestCase(XenForoIndex, "data-template=\"forum_list\"", "data-template=\"forum_view\"")]
        [TestCase(AmphoroIndex, "template-forum_list", "template-forum_view")]
        public void EnsureTemplate_OfAnotherTemplate_StopsTheCrawl(string html, string marker, string other)
        {
            var error = Assert.Throws<UnexpectedPageException>(() => SmogonPages.EnsureTemplate(html.Replace(marker, other), Url, "forum_list"));
            Assert.That(error!.Message, Does.Contain("expected the XenForo template 'forum_list', the page is 'forum_view'"));
        }

        [TestCase(SmogonHtml.BotCheck)]
        [TestCase(SmogonHtml.BotCheckForumGate)]
        public void EnsureTemplate_OfABotCheck_ReportsTheBotCheck(string html)
        {
            Assert.That(SmogonPages.IsBotCheck(html), Is.True);
            Assert.Throws<SmogonAccessBlockedException>(() => SmogonPages.EnsureTemplate(html, Url, "forum_list"));
            Assert.Throws<SmogonAccessBlockedException>(() => SmogonPages.EnsureThreadPage(html, Url));
        }

        [Test]
        public void IsBotCheck_KnowsTheCheckByItsTextAlone()
        {
            var html = SmogonHtml.BotCheckForumGate.Replace("forum_gate=", "gate_cookie=");

            Assert.That(SmogonPages.IsBotCheck(html), Is.True);
        }

        [TestCase(XenForoIndex)]
        [TestCase(AmphoroIndex)]
        public void IsBotCheck_OfAForumPage_IsFalse_EvenIfAPostQuotesTheCheck(string html)
        {
            Assert.That(SmogonPages.IsBotCheck(html), Is.False);
            Assert.That(SmogonPages.IsBotCheck(html.Replace("<p>Forums</p>", "<p>It said: Checking your browser...</p>")), Is.False);
            Assert.That(SmogonPages.IsBotCheck(html.Replace("<p>Forums</p>", "<p>document.cookie = \"forum_gate=\" + n</p>")), Is.False);
        }

        [Test]
        public void GetNodeId_ReadsTheContainerKey_OrSinceNovember2026TheQuickSearch()
        {
            const string xenForo = "<html id=\"XF\" data-template=\"forum_view\"\n\tdata-container-key=\"node-726\"\n\tdata-content-key=\"forum-726\">";
            const string amphoro = "<html id=\"forum\" class=\"template-forum_view\">\n<good-select selected=\"{&quot;search_type&quot;:&quot;post&quot;,&quot;c&quot;:{&quot;nodes&quot;:[958],&quot;child_nodes&quot;:1}}\">";
            const string unencoded = "<option value='{\"search_type\":\"post\",\"c\":{\"nodes\":[31],\"child_nodes\":1}}'>This forum</option>";

            Assert.That(SmogonPages.GetNodeId(xenForo), Is.EqualTo(726));
            Assert.That(SmogonPages.GetNodeId(amphoro), Is.EqualTo(958));
            Assert.That(SmogonPages.GetNodeId(unencoded), Is.EqualTo(31));
            Assert.That(SmogonPages.GetNodeId(xenForo + amphoro), Is.EqualTo(726));
            Assert.That(SmogonPages.GetNodeId("<html id=\"forum\" class=\"template-forum_view\">"), Is.Null);
        }
    }
}
