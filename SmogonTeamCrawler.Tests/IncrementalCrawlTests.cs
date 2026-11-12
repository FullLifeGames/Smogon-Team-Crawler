#nullable enable
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class IncrementalCrawlTests
    {
        private const string ForumUrl = "https://www.smogon.com/forums/forums/overused.726/";
        private const string Tier = "gen9OverUsed";
        private static readonly DateTime Old = new(2020, 5, 1, 13, 37, 0);

        private FakeHttpHandler _http = null!;
        private IDistributedCache _cache = null!;
        private FakeForum _forum = null!;
        private FakeThread _sticky = null!;
        private FakeThread _b = null!;
        private FakeThread _c = null!;
        private FakeThread _d = null!;

        [SetUp]
        public void SetUp()
        {
            _http = new FakeHttpHandler();
            _cache = new InMemoryDistributedCache();
            _forum = new FakeForum(ForumUrl) { ThreadsPerPage = 2 };
            _sticky = _forum.AddThread(1, sticky: true).AddPage(new FakePost(11, Old.AddHours(5), "A1"));
            _b = _forum.AddThread(2)
                .AddPage(new FakePost(21, Old.AddHours(1), "B1"))
                .AddPage(new FakePost(22, Old.AddHours(4), "B2"));
            _c = _forum.AddThread(3).AddPage(new FakePost(31, Old.AddHours(3), "C1"));
            _d = _forum.AddThread(4).AddPage(new FakePost(41, Old.AddHours(2)));
        }

        private async Task<List<Team>> Crawl(CollectorOptions? options = null, Action? afterPublish = null)
        {
            _forum.Publish(_http);
            afterPublish?.Invoke();
            _http.Requests.Clear();
            options ??= new CollectorOptions();
            // The listing walk; SearchCrawlTests covers the search.
            options.UseSearch = false;
            options.HttpClient = new HttpClient(_http);
            var collected = await new TeamCollector(_cache, options)
                .Collect(new Dictionary<string, string> { [Tier] = ForumUrl }, false);
            return collected.TryGetValue(Tier, out var teams) ? teams.ToList() : new List<Team>();
        }

        /// <summary>The tag of each team ("MonB2x1 @ ..." is "B2"), sorted.</summary>
        private static string[] Tags(IEnumerable<Team> teams) => teams
            .Select(t => t.TeamString.TrimStart())
            .Select(text => text[3..text.IndexOf('x')])
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        [Test]
        public async Task FirstRun_ReadsAllPages_WithoutPage1Suffix()
        {
            var teams = await Crawl();

            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "C1" }));
            Assert.That(_http.Requests, Is.EquivalentTo(new[]
            {
                ForumUrl, ForumUrl + "page-2",
                _sticky.Url, _b.Url, _b.Url + "page-2", _c.Url, _d.Url,
            }));
            Assert.That(_http.Requests, Has.None.Contains("page-1"));
            Assert.That(teams.Single(t => t.TeamString.StartsWith("MonB2")).URL, Is.EqualTo(_b.Url + "page-2#post-22"));
        }

        [Test]
        public async Task SecondRun_WithoutChanges_ReadsOnlyTheFirstListingPage()
        {
            await Crawl();
            var teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "C1" }));
        }

        [Test]
        public async Task NewPost_ReadsOnlyTheLastKnownAndTheNewPages()
        {
            await Crawl();
            _b.AddPage(new FakePost(23, Old.AddDays(10), "B3"));

            var teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _b.Url + "page-2", _b.Url + "page-3" }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "B3", "C1" }));
        }

        [Test]
        public async Task NewPost_OnAnOlderListingPage_IsFoundBecauseTheThreadMovesToTheTop()
        {
            await Crawl();
            _d.Pages[0].Add(new FakePost(42, Old.AddDays(10), "D2"));

            var teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _d.Url }));
            Assert.That(Tags(teams), Does.Contain("D2"));
        }

        [Test]
        public async Task NewPost_AlsoRereadsPagesWithRecentPosts()
        {
            var now = DateTime.Now;
            var recent = _forum.AddThread(5)
                .AddPage(new FakePost(51, now.AddDays(-3), "R1"))
                .AddPage(new FakePost(52, now.AddDays(-2), "R2"));
            await Crawl();
            recent.Pages[1].Add(new FakePost(53, now.AddHours(-1), "R3"));

            await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, recent.Url, recent.Url + "page-2" }));
        }

        [Test]
        public async Task NewPost_WithRecentRefreshTurnedOff_ReadsOnlyTheLastPage()
        {
            var now = DateTime.Now;
            var recent = _forum.AddThread(5)
                .AddPage(new FakePost(51, now.AddDays(-3), "R1"))
                .AddPage(new FakePost(52, now.AddDays(-2), "R2"));
            await Crawl();
            recent.Pages[1].Add(new FakePost(53, now.AddHours(-1), "R3"));

            await Crawl(new CollectorOptions { RecentPageRefreshWindow = TimeSpan.Zero });

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, recent.Url + "page-2" }));
        }

        [Test]
        public async Task ThreadWithDeletedPosts_DropsPagesThatNoLongerExist()
        {
            _b.AddPage(new FakePost(23, Old.AddHours(4).AddMinutes(30), "B3"));
            await Crawl();
            _b.Pages.RemoveAt(2);
            _b.Pages[1].Add(new FakePost(24, Old.AddDays(10)));

            var teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _b.Url + "page-2" }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "C1" }));
        }

        [Test]
        public async Task BotCheck_StopsTheCrawl_AndKeepsTheStoredState()
        {
            await Crawl();
            _b.AddPage(new FakePost(23, Old.AddDays(10), "B3"));

            Assert.ThrowsAsync<SmogonAccessBlockedException>(() =>
                Crawl(afterPublish: () => _http.Html(_b.Url + "page-2", SmogonHtml.BotCheck)));

            // The interrupted thread still counts as changed and is read again in full once access works.
            var teams = await Crawl();
            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _b.Url + "page-2", _b.Url + "page-3" }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "B3", "C1" }));
        }

        [Test]
        public void BotCheck_OnTheListing_StopsTheCrawl()
        {
            Assert.ThrowsAsync<SmogonAccessBlockedException>(() =>
                Crawl(afterPublish: () => _http.Html(ForumUrl, SmogonHtml.BotCheck)));
        }

        [Test]
        public void UnexpectedListingMarkup_StopsTheCrawl()
        {
            Assert.ThrowsAsync<UnexpectedPageException>(() =>
                Crawl(afterPublish: () => _http.Html(ForumUrl, "<html><body>Maintenance</body></html>")));
        }

        [Test]
        public async Task UnexpectedThreadPage_SkipsOnlyThatThread_AndRetriesItNextRun()
        {
            var teams = await Crawl(afterPublish: () => _http.Html(_c.Url, "<html><body>Something else</body></html>"));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2" }));

            teams = await Crawl();
            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _c.Url }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "C1" }));
        }

        [Test]
        public async Task UnexpectedThreadPage_BeyondWhereTheListingStops_IsRetriedNextRun()
        {
            // D is on listing page 2, which the second run does not read.
            var teams = await Crawl(afterPublish: () => _http.Html(_d.Url, "<html><body>Something else</body></html>"));
            Assert.That(_http.Requests, Does.Contain(_d.Url));

            _d.Pages[0].Add(new FakePost(42, Old.AddHours(2).AddMinutes(1), "D2"));
            teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, _d.Url }));
            Assert.That(Tags(teams), Does.Contain("D2"));

            await Crawl();
            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl }));
        }

        [Test]
        public void ManyUnexpectedThreadPagesInARow_StopTheCrawl()
        {
            for (var id = 100; id < 105; id++)
            {
                _forum.AddThread(id).AddPage(new FakePost(id * 10, Old.AddDays(1)));
            }

            Assert.ThrowsAsync<UnexpectedPageException>(() => Crawl(afterPublish: () =>
            {
                for (var id = 100; id < 105; id++)
                {
                    _http.Html(SmogonHtml.ThreadUrl(id), "<html><body>Something else</body></html>");
                }
            }));
        }

        [Test]
        public async Task FullRefresh_ReadsEverythingAgain()
        {
            await Crawl();
            await Crawl(new CollectorOptions { FullRefresh = true });

            Assert.That(_http.Requests, Has.Count.EqualTo(7));
        }

        [Test]
        public async Task Paste_IsFetchedOnlyOnce()
        {
            const string pasteUrl = "https://pokepast.es/abcdef123456";
            var withPaste = _forum.AddThread(6).AddPage(new FakePost(61, Old.AddHours(6), "P1", pasteUrl));
            await Crawl();
            Assert.That(_http.Requests, Does.Contain(pasteUrl));

            withPaste.Pages[0].Add(new FakePost(62, Old.AddDays(10)));
            var teams = await Crawl();

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, withPaste.Url }));
            Assert.That(Tags(teams), Does.Contain("P1"));
        }

        [Test]
        public async Task Pastes_AreFetchedWhileTheNextPageIsRead()
        {
            const string pasteUrl = "https://pokepast.es/0123456789abcdef";
            var thread = _forum.AddThread(7)
                .AddPage(new FakePost(71, Old.AddHours(6), "P7", pasteUrl))
                .AddPage(new FakePost(72, Old.AddHours(7), "Q7"));
            // The paste answers only once page 2 was requested: reading pages one after another would wait forever.
            var page2Requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _http.OnRequest = url => { if (url == thread.Url + "page-2") page2Requested.TrySetResult(); };
            _http.Hold(pasteUrl, page2Requested.Task);

            var teams = await Crawl().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(Tags(teams), Does.Contain("P7").And.Contain("Q7"));
            var state = JsonConvert.DeserializeObject<ThreadCrawlState>(_cache.GetString("stc:v3:thread:7")!)!;
            Assert.That(state.Pages[1].Teams, Has.Count.EqualTo(1), "page 1 is stored with the team of its paste");
        }

        [Test]
        public async Task PasteOnTwoPages_IsFetchedOnce_WhileStillOnItsWay()
        {
            const string pasteUrl = "https://pokepast.es/fedcba9876543210";
            var thread = _forum.AddThread(8)
                .AddPage(new FakePost(81, Old.AddHours(6), "R8", pasteUrl))
                .AddPage(new FakePost(82, Old.AddHours(7), "R8", pasteUrl));
            var page2Requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _http.OnRequest = url => { if (url == thread.Url + "page-2") page2Requested.TrySetResult(); };
            // Page 2 is parsed while the first request for the paste is still open.
            _http.Hold(pasteUrl, page2Requested.Task.ContinueWith(_ => Task.Delay(300)).Unwrap());

            var teams = await Crawl().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(_http.Requests.Count(url => url == pasteUrl), Is.EqualTo(1));
            Assert.That(Tags(teams).Count(tag => tag == "R8"), Is.EqualTo(2));
        }

        [Test]
        public async Task ShowdownTeamLink_IsCollected_AndFetchedOnlyOnce()
        {
            const string teamLink = "https://psim.us/t/2075402";
            var apiUrl = ShowdownTeams.ApiUrl("2075402", null);
            var thread = _forum.AddThread(10).AddPage(new FakePost(101, Old.AddHours(6), PasteUrl: teamLink));
            var answer = "]" + JsonConvert.SerializeObject(new { teamid = "2075402", team = ShowdownTeamsTests.PackedTeam });
            void Serve()
            {
                _http.Html(apiUrl, answer);
                _http.Html(ShowdownNames.DataUrls[2], """exports.BattleItems = {boosterenergy:{name:"Booster Energy"}};""");
            }

            var teams = await Crawl(afterPublish: Serve);

            var team = teams.Single(t => t.URL == thread.Url + "page-1#post-101");
            Assert.That(team.TeamString, Does.StartWith("Roaring Moon @ Booster Energy\n"));
            Assert.That(_http.Requests, Does.Contain(apiUrl).And.Contain(ShowdownNames.DataUrls[0]), "the data files of unknown names answer 404 and only cost nicer names");
            Assert.That(_http.Requests, Has.None.Contains("psim.us"));

            thread.Pages[0].Add(new FakePost(102, Old.AddDays(10)));
            await Crawl(afterPublish: Serve);

            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl, thread.Url }));
        }

        [Test]
        public async Task PrivateShowdownTeam_IsNoTeam()
        {
            var thread = _forum.AddThread(10).AddPage(new FakePost(101, Old.AddHours(6), PasteUrl: "https://psim.us/t/2075556"));

            var teams = await Crawl(afterPublish: () => _http.Html(ShowdownTeams.ApiUrl("2075556", null), """]{"team":null}"""));

            Assert.That(teams.Where(t => t.URL!.StartsWith(thread.Url)), Is.Empty);
        }

        [Test]
        public async Task MovedThread_IsOnlyCollectedInItsNewForum()
        {
            const string otherForumUrl = "https://www.smogon.com/forums/forums/gen-9-ou-archive.841/";
            var otherForum = new FakeForum(otherForumUrl);
            otherForum.AddThread(9).AddPage(new FakePost(91, Old, "Z1"));
            otherForum.Publish(_http);
            var forums = new Dictionary<string, string> { [Tier] = ForumUrl, ["gen9Gen 9 OU Archive"] = otherForumUrl };

            _forum.Publish(_http);
            var options = new CollectorOptions { HttpClient = new HttpClient(_http), UseSearch = false };
            await new TeamCollector(_cache, options).Collect(forums, false);

            _forum.Threads.Remove(_c);
            otherForum.Threads.Add(_c);
            _forum.Publish(_http);
            otherForum.Publish(_http);
            var collected = await new TeamCollector(_cache, options).Collect(forums, false);

            Assert.That(Tags(collected[Tier]), Is.EqualTo(new[] { "A1", "B1", "B2" }));
            Assert.That(Tags(collected["gen9Gen 9 OU Archive"]), Is.EqualTo(new[] { "C1", "Z1" }));
        }

        [Test]
        public async Task RmtForum_GroupsTeamsByThreadPrefix()
        {
            _forum.Threads.Clear();
            _forum.AddThread(7, prefix: "SV OU").AddPage(new FakePost(71, Old, "OU1"));
            _forum.AddThread(8, prefix: "SV UU").AddPage(new FakePost(81, Old, "UU1"));
            _forum.Publish(_http);

            var collected = await new TeamCollector(_cache, new CollectorOptions { HttpClient = new HttpClient(_http), UseSearch = false })
                .Collect(new Dictionary<string, string> { ["SV OU Teams"] = ForumUrl }, true);

            Assert.That(Tags(collected["SV OU"]), Is.EqualTo(new[] { "OU1" }));
            Assert.That(Tags(collected["SV UU"]), Is.EqualTo(new[] { "UU1" }));
        }

        [Test]
        public async Task LegacyEntry_ThatIsStillCurrent_IsTakenOverWithoutRequests()
        {
            StoreLegacy(_c, numberOfPages: 1, lastPost: _c.LatestPost, ("legacyC", 1));

            var teams = await Crawl();

            Assert.That(_http.Requests, Has.None.StartsWith(_c.Url));
            Assert.That(Tags(teams), Does.Contain("legacyC").And.Not.Contain("C1"));

            await Crawl();
            Assert.That(_http.Requests, Is.EqualTo(new[] { ForumUrl }));
        }

        [Test]
        public async Task LegacyEntry_ThatIsOutdated_IsReadFromItsLastKnownPage()
        {
            _b.AddPage(new FakePost(23, Old.AddDays(10), "B3"));
            StoreLegacy(_b, numberOfPages: 2, lastPost: Old.AddHours(4), ("legacyB1", 1), ("legacyB2", 2));

            var teams = await Crawl();

            Assert.That(_http.Requests.Where(url => url.StartsWith(_b.Url)), Is.EqualTo(new[] { _b.Url + "page-2", _b.Url + "page-3" }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B2", "B3", "C1", "legacyB1" }));
        }

        private void StoreLegacy(FakeThread thread, int numberOfPages, DateTime lastPost, params (string Tag, int Page)[] teams)
        {
            var legacy = new ThreadAnalyzeResult
            {
                CollectedTeams = teams
                    .Select(t => new Team(SmogonHtml.TeamText(t.Tag, ""), 0, Old, thread.Url + "page-" + t.Page + "#post-1", "Old", ""))
                    .ToList(),
                NumberOfPages = numberOfPages,
                LastPost = lastPost,
            };
            _cache.SetString(thread.Url, JsonConvert.SerializeObject(legacy));
        }
    }
}
