#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;

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

        [Test]
        public void IsBotCheck_KnowsTheGateCookieOf2026_11_24_ButNotAPostAboutIt()
        {
            Assert.That(SmogonPages.IsBotCheck(SmogonHtml.BotCheck.Replace("xf_smogon_gate", "forum_gate").Replace("Checking your browser...", "")), Is.True);
            Assert.That(SmogonPages.IsBotCheck("<html class=\"template-thread_view\"><p>Checking your browser... \"forum_gate=\"</p></html>"), Is.False);
        }
    }
}
