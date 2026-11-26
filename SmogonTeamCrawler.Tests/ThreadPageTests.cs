#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class ThreadPageTests
    {
        [Test]
        public async Task Page_Of2026_11_26_GivesTheAuthorsAndDatesOf2026_11_25()
        {
            // Each paste answers with a team tagged with its ID ("Mon97119fb9ac5c7851x1 @ ...").
            var http = new FakeHttpHandler();
            http.Prefix("https://pokepast.es/", url => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SmogonHtml.Pokepaste(url["https://pokepast.es/".Length..])),
            });
            var collector = new TeamCollector(new InMemoryDistributedCache(), new CollectorOptions { HttpClient = new HttpClient(http) });

            var page = await collector.AnalyzePageAsync(SavedPages.ThreadPageUrl, 29, SavedPages.ThreadPage, "", DateTime.UtcNow);

            // The 19 teams of the run of 2026-11-25 (author and likes as in its log, the old markup), and one posted since
            var teams = page.Teams.Select(team => (Post: team.URL[(team.URL.IndexOf('#') + 1)..], team.PostedBy, team.Likes, team.PostDate)).ToList();
            Assert.That(teams.Distinct(), Is.EqualTo(new[]
            {
                ("post-11186177", "Sami", 5, new DateTime(2026, 11, 5, 13, 48, 0)),
                ("post-11190622", "Sami", 4, new DateTime(2026, 11, 10, 11, 40, 0)),
                ("post-11190675", "Ebony God", 5, new DateTime(2026, 11, 10, 12, 50, 0)),
                ("post-11190702", "TheShowdownHero", 3, new DateTime(2026, 11, 10, 13, 24, 0)),
                ("post-11192126", "Cancel cult", 2, new DateTime(2026, 11, 12, 15, 44, 0)),
                ("post-11192645", "Sami", 3, new DateTime(2026, 11, 13, 12, 20, 0)),
                ("post-11197006", "Sami", 2, new DateTime(2026, 11, 18, 7, 42, 0)),
                ("post-11200680", "Sami", 0, new DateTime(2026, 11, 23, 6, 52, 0)),
                ("post-11200683", "OatIsHere", 1, new DateTime(2026, 11, 23, 6, 57, 0)),
                ("post-11200684", "Sami", 1, new DateTime(2026, 11, 23, 7, 8, 0)),
                ("post-11201742", "TheShowdownHero", 0, new DateTime(2026, 11, 24, 12, 25, 0)),
                ("post-11202459", "Cancel cult", 0, new DateTime(2026, 11, 25, 15, 3, 0)),
            }));
            Assert.That(teams, Has.Count.EqualTo(20));
            Assert.That(teams.Count(team => team.Post == "post-11186177"), Is.EqualTo(6));
            Assert.That(teams.Count(team => team.Post == "post-11197006"), Is.EqualTo(4));
            Assert.That(page.Teams.All(team => team.URL.StartsWith(SavedPages.ThreadPageUrl + "page-29#post-")), Is.True);
            Assert.That(page.NewestPostDate, Is.EqualTo(new DateTime(2026, 11, 25, 15, 3, 0)));
            // Two links in one line: the first is the team, as before
            Assert.That(page.Teams.Single(team => team.URL.EndsWith("#post-11190622")).TeamString.TrimStart(), Does.StartWith("Monecaf7796eaa548e8x1 @"));
        }
    }
}
