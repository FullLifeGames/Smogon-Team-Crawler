#nullable enable
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    /// <summary>Runs that find team posts through the search: all posts of a forum on its first run, later the posts since.</summary>
    [TestFixture]
    public class SearchCrawlTests
    {
        private const string ForumUrl = "https://www.smogon.com/forums/forums/overused.726/";
        private const string OtherForumUrl = "https://www.smogon.com/forums/forums/uploaded-analyses.769/";
        private const string SlugForumUrl = "https://www.smogon.com/forums/forums/adv/";
        private const string Tier = "gen9OverUsed";
        private const string OtherTier = "gen9Uploaded Analyses";
        private static readonly DateTime Old = new(2020, 5, 1, 13, 37, 0);

        private FakeHttpHandler _http = null!;
        private IDistributedCache _cache = null!;
        private FakeForum _forum = null!;
        private FakeForum _other = null!;
        private FakeForum _slug = null!;
        private FakeSearch _search = null!;
        private FakeThread _a = null!;
        private FakeThread _b = null!;
        private FakeThread _c = null!;
        private DateTime _recent;

        [SetUp]
        public void SetUp()
        {
            _http = new FakeHttpHandler();
            _cache = new InMemoryDistributedCache();
            _forum = new FakeForum(ForumUrl);
            _a = _forum.AddThread(1).AddPage(new FakePost(11, Old, "A1"));
            _b = _forum.AddThread(2)
                .AddPage(new FakePost(21, Old.AddHours(1), "B1"))
                .AddPage(new FakePost(22, Old.AddHours(2), "B2", "https://pokepast.es/b2"));
            _c = _forum.AddThread(3).AddPage(new FakePost(31, Old.AddHours(3)));
            _other = new FakeForum(OtherForumUrl);
            _slug = new FakeForum(SlugForumUrl, nodeId: 958);
            _search = new FakeSearch(_forum, _other, _slug);
            // Posted after the first run, which FirstRun moves back in time
            _recent = DateTime.UtcNow.AddHours(-1);
        }

        private async Task<IDictionary<string, ICollection<Team>>> CrawlForums(Dictionary<string, string> forums, CollectorOptions? options = null)
        {
            _forum.Publish(_http);
            _other.Publish(_http);
            _slug.Publish(_http);
            _search.Publish(_http);
            _http.Requests.Clear();
            options ??= new CollectorOptions();
            options.HttpClient = new HttpClient(_http);
            return await new TeamCollector(_cache, options).Collect(forums, false);
        }

        private async Task<List<Team>> Crawl(CollectorOptions? options = null)
        {
            var collected = await CrawlForums(new Dictionary<string, string> { [Tier] = ForumUrl }, options);
            return collected.TryGetValue(Tier, out var teams) ? teams.ToList() : new List<Team>();
        }

        /// <summary>The first run, by search. It is moved back by <paramref name="age"/> (default a day), so that posts from the last hour are new.</summary>
        private async Task FirstRun(TimeSpan? age = null, params string[] forumUrls)
        {
            forumUrls = forumUrls.Length > 0 ? forumUrls : [ForumUrl];
            await CrawlForums(forumUrls.ToDictionary(url => url == ForumUrl ? Tier : OtherTier, url => url));
            var by = age ?? TimeSpan.FromDays(1);
            foreach (var url in forumUrls)
            {
                MoveBack(url, by);
            }
        }

        private void MoveBack(string forumUrl, TimeSpan by)
        {
            var forumKey = "stc:v3:forum:" + forumUrl;
            var forum = JsonConvert.DeserializeObject<ForumCrawlState>(_cache.GetString(forumKey)!)!;
            forum.LastCrawl -= by;
            forum.Watermark -= (long)by.TotalSeconds;
            _cache.SetString(forumKey, JsonConvert.SerializeObject(forum));
            foreach (var threadId in forum.Threads.Keys)
            {
                UpdateThread(threadId, thread =>
                {
                    foreach (var page in thread.Pages.Values)
                    {
                        page.FetchedAt -= by;
                    }
                });
            }
        }

        private void UpdateThread(long threadId, Action<ThreadCrawlState> change)
        {
            var key = "stc:v3:thread:" + threadId;
            var thread = JsonConvert.DeserializeObject<ThreadCrawlState>(_cache.GetString(key)!)!;
            change(thread);
            _cache.SetString(key, JsonConvert.SerializeObject(thread));
        }

        private ThreadCrawlState Thread(long threadId) => JsonConvert.DeserializeObject<ThreadCrawlState>(_cache.GetString("stc:v3:thread:" + threadId)!)!;

        private List<string> SearchRequests() => _http.Requests.Where(url => url.Contains("/forums/search/")).ToList();

        /// <summary>The first result page of each search for <paramref name="query"/>, without the pages after it.</summary>
        private List<string> Searches(string query) => _http.Requests
            .Select(Uri.UnescapeDataString)
            .Where(url => url.StartsWith("https://www.smogon.com/forums/search/1/?q=" + query + "&"))
            .ToList();

        private List<string> OtherRequests() => _http.Requests.Where(url => !url.Contains("/forums/search/")).ToList();

        /// <summary>The tag of each team ("MonB2x1 @ ..." is "B2"), sorted.</summary>
        private static string[] Tags(IEnumerable<Team> teams) => teams
            .Select(t => t.TeamString.TrimStart())
            .Select(text => text[3..text.IndexOf('x')])
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        [Test]
        public async Task FirstRun_ReadsOnlyThePagesWithHits()
        {
            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.EquivalentTo(new[] { _a.Url, _b.Url, _b.Url + "page-2", "https://pokepast.es/b2" }), "no listing, no thread without team");
            Assert.That(SearchRequests(), Has.Count.EqualTo(new CollectorOptions().SearchQueries.Count), "one result page per query");
            Assert.That(SearchRequests(), Has.All.Contain("&c[nodes][0]=726").And.All.Not.Contain("newer_than"));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2" }));
        }

        [Test]
        public async Task FirstRun_OfAForumWithoutIdInItsUrl_ReadsTheIdFromItsListing()
        {
            var thread = _slug.AddThread(70).AddPage(new FakePost(701, Old, "D1", "https://pokepast.es/d1"));
            var forums = new Dictionary<string, string> { ["gen8ADV"] = SlugForumUrl };

            var teams = await CrawlForums(forums);

            Assert.That(OtherRequests(), Is.EqualTo(new[] { SlugForumUrl, thread.Url, "https://pokepast.es/d1" }));
            Assert.That(SearchRequests(), Has.All.Contain("&c[nodes][0]=958"));
            Assert.That(Tags(teams["gen8ADV"]), Is.EqualTo(new[] { "D1" }));

            await CrawlForums(forums);
            Assert.That(OtherRequests(), Is.Empty, "the ID is stored");
        }

        [Test]
        public async Task ForumOfGenerations1And2_IsWalked()
        {
            _slug.AddThread(70).AddPage(new FakePost(701, Old, "R1"));

            var teams = await CrawlForums(new Dictionary<string, string> { ["gen8RBY"] = SlugForumUrl });

            Assert.That(SearchRequests(), Is.Empty);
            Assert.That(_http.Requests, Does.Contain(SlugForumUrl));
            Assert.That(Tags(teams["gen8RBY"]), Is.EqualTo(new[] { "R1" }));
        }

        [Test]
        public async Task FirstRun_SearchNotAvailable_WalksTheListing()
        {
            _search.Unavailable = "<!DOCTYPE html><html data-template=\"error\"><body><div class=\"blockMessage\">The search could not be completed because the search keywords were too short, too long, or too common.</div></body></html>";

            var teams = await Crawl();

            Assert.That(SearchRequests(), Has.Count.EqualTo(1));
            Assert.That(_http.Requests, Does.Contain(ForumUrl).And.Contain(_c.Url));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2" }));
        }

        [Test]
        public async Task FirstRun_WithThreadsOfVersion2_WalksTheListing()
        {
            var legacy = new ThreadAnalyzeResult
            {
                CollectedTeams = [new Team(SmogonHtml.TeamText("legacyB", ""), 0, Old, _b.Url + "page-1#post-21", "Old", "")],
                NumberOfPages = 2,
                LastPost = _b.LatestPost,
            };
            _cache.SetString(_b.Url, JsonConvert.SerializeObject(legacy));

            var teams = await Crawl();

            Assert.That(_http.Requests, Does.Contain(ForumUrl).And.None.StartWith(_b.Url));
            Assert.That(Tags(teams), Does.Contain("legacyB"));
        }

        [Test]
        public async Task LaterRun_ReadsOnlyThePagesWithHits()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            _c.Pages[0].Add(new FakePost(32, _recent));
            var e = _forum.AddThread(5, prefix: "RMT").AddPage(new FakePost(51, _recent.AddMinutes(1), "E1", "https://pokepast.es/e1"));

            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.EquivalentTo(new[] { _b.Url + "page-3", "https://pokepast.es/b3", e.Url, "https://pokepast.es/e1" }));
            Assert.That(SearchRequests(), Has.Count.EqualTo(new CollectorOptions().SearchQueries.Count), "one result page per query");
            Assert.That(SearchRequests(), Has.All.Contain("&c[newer_than]="));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "B3", "E1" }));
            var forum = JsonConvert.DeserializeObject<ForumCrawlState>(_cache.GetString("stc:v3:forum:" + ForumUrl)!)!;
            Assert.That(forum.Threads[5].Prefix, Is.EqualTo("RMT"));
        }

        [Test]
        public async Task LaterRun_SearchesAllForumsAtOnce()
        {
            await FirstRun(forumUrls: [ForumUrl, OtherForumUrl]);
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            _other.AddThread(9).AddPage(new FakePost(91, _recent, "Z1", "https://pokepast.es/z1"));

            var teams = await CrawlForums(new Dictionary<string, string> { [Tier] = ForumUrl, [OtherTier] = OtherForumUrl });

            Assert.That(SearchRequests(), Has.Count.EqualTo(new CollectorOptions().SearchQueries.Count));
            Assert.That(SearchRequests(), Has.All.Contain("&c[nodes][0]=726&c[nodes][1]=769"));
            Assert.That(Tags(teams[Tier]), Does.Contain("B3").And.Not.Contain("Z1"));
            Assert.That(Tags(teams[OtherTier]), Is.EqualTo(new[] { "Z1" }));
        }

        [Test]
        public async Task TeamWrittenIntoAPost_IsFoundByItsNatures()
        {
            await FirstRun();
            _c.Pages[0].Add(new FakePost(32, _recent, "C2"));

            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.EqualTo(new[] { _c.Url }));
            Assert.That(Tags(teams), Does.Contain("C2"));
        }

        [Test]
        public async Task HitInAForumThatIsNotCrawled_IsNotRead()
        {
            await FirstRun();
            _other.AddThread(9).AddPage(new FakePost(91, _recent, "Z1", "https://pokepast.es/z1"));

            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.Empty);
            Assert.That(Tags(teams), Does.Not.Contain("Z1"));
        }

        [Test]
        public async Task HitOnAPageReadSinceThePost_IsNotReadAgain()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            await Crawl();

            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.Empty);
            Assert.That(Tags(teams), Does.Contain("B3"));
        }

        [Test]
        public async Task SearchNotAvailable_WalksTheListing_AndReadsOnlyThreadsWithNewPosts()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            _search.Unavailable = "<!DOCTYPE html><html data-template=\"error\"><body>You must be logged-in to do that.</body></html>";

            var teams = await Crawl();

            Assert.That(SearchRequests(), Has.Count.EqualTo(1));
            // Thread 3 has no teams and thread 1 no new posts since the first run's search
            Assert.That(OtherRequests(), Is.EqualTo(new[] { ForumUrl, _b.Url + "page-2", _b.Url + "page-3", "https://pokepast.es/b3" }));
            Assert.That(Tags(teams), Is.EqualTo(new[] { "A1", "B1", "B2", "B3" }));
        }

        [Test]
        public async Task MoreHitsThanASearchShows_AreFoundByASearchForTheDaysBefore()
        {
            await FirstRun(TimeSpan.FromDays(4));
            var dayBefore = DateTime.UtcNow.Date.AddDays(-2).AddHours(12);
            var lastDay = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
            var thread = _forum.AddThread(6);
            for (var i = 0; i < 25; i++)
            {
                thread.AddPage(new FakePost(600 + i, (i < 10 ? dayBefore : lastDay).AddMinutes(i), "S" + i, "https://pokepast.es/s" + i));
            }
            _search.ResultsPerPage = 1;
            _search.MaxResults = PostSearch.MaxResultPages;

            var teams = await Crawl();

            var pasteSearches = Searches(CollectorOptions.PasteQuery);
            Assert.That(pasteSearches, Has.Count.EqualTo(2));
            Assert.That(pasteSearches[1], Does.Contain("&c[older_than]=" + lastDay.ToString("yyyy-MM-dd")));
            Assert.That(Tags(teams).Count(tag => tag.StartsWith('S')), Is.EqualTo(25));
            Assert.That(_http.Requests, Does.Not.Contain(ForumUrl));
        }

        [Test]
        public async Task OneDayWithMoreHitsThanASearchShows_WalksTheListing()
        {
            await FirstRun(TimeSpan.FromDays(3));
            var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
            var thread = _forum.AddThread(6);
            for (var i = 0; i < 21; i++)
            {
                thread.AddPage(new FakePost(600 + i, day.AddMinutes(i), "S" + i, "https://pokepast.es/s" + i));
            }
            _search.ResultsPerPage = 1;
            _search.MaxResults = PostSearch.MaxResultPages;

            var teams = await Crawl();

            Assert.That(_http.Requests, Does.Contain(ForumUrl));
            Assert.That(Tags(teams).Count(tag => tag.StartsWith('S')), Is.EqualTo(21));
        }

        [Test]
        public async Task OneDayWithMoreHitsThanASearchShows_InSeveralForums_IsSearchedForumByForum()
        {
            await FirstRun(TimeSpan.FromDays(3), ForumUrl, OtherForumUrl);
            var day = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
            var here = _forum.AddThread(6);
            var there = _other.AddThread(7);
            for (var i = 0; i < 12; i++)
            {
                here.AddPage(new FakePost(600 + i, day.AddMinutes(2 * i), "S" + i, "https://pokepast.es/s" + i));
                there.AddPage(new FakePost(700 + i, day.AddMinutes(2 * i + 1), "T" + i, "https://pokepast.es/t" + i));
            }
            _search.ResultsPerPage = 1;
            _search.MaxResults = PostSearch.MaxResultPages;

            var teams = await CrawlForums(new Dictionary<string, string> { [Tier] = ForumUrl, [OtherTier] = OtherForumUrl });

            Assert.That(_http.Requests, Does.Not.Contain(ForumUrl).And.Not.Contain(OtherForumUrl));
            Assert.That(Searches(CollectorOptions.PasteQuery).Select(url => url[url.IndexOf("&c[nodes]")..]),
                Is.EqualTo(new[] { "&c[nodes][0]=726&c[nodes][1]=769", "&c[nodes][0]=726&c[nodes][1]=769", "&c[nodes][0]=726", "&c[nodes][0]=769" }));
            Assert.That(Tags(teams[Tier]).Count(tag => tag.StartsWith('S')), Is.EqualTo(12));
            Assert.That(Tags(teams[OtherTier]).Count(tag => tag.StartsWith('T')), Is.EqualTo(12));
        }

        [Test]
        public async Task RecentTeam_IsReadAgainOnceTwiceAsOld()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            await Crawl();
            // Read 10 minutes after the post, an hour after it the likes are due
            UpdateThread(2, thread => thread.Pages[3].FetchedAt = _recent.AddMinutes(10));

            await Crawl();
            Assert.That(OtherRequests(), Is.EqualTo(new[] { _b.Url + "page-3" }));

            await Crawl();
            Assert.That(OtherRequests(), Is.Empty);
        }

        [Test]
        public async Task ListingWalkAfterSearchRuns_ReadsThePagesTheSearchMissed()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            _b.AddPage(new FakePost(24, _recent.AddMinutes(1), "B4"));
            _search.NotIndexed.Add(24);
            var searched = await Crawl();
            Assert.That(Tags(searched), Does.Not.Contain("B4"));
            Assert.That(Thread(2).NumberOfPages, Is.EqualTo(2), "the listing walk keeps its place");

            var walked = await Crawl(new CollectorOptions { UseSearch = false });

            Assert.That(OtherRequests(), Is.EqualTo(new[] { ForumUrl, _b.Url + "page-2", _b.Url + "page-3", _b.Url + "page-4" }));
            Assert.That(Tags(walked), Does.Contain("B3").And.Contain("B4"));
        }

        [Test]
        public async Task PostOnAnotherPageThanItsPosition_IsFoundThroughItsLink()
        {
            await FirstRun();
            _b.AddPage(new FakePost(23, _recent, "B3", "https://pokepast.es/b3"));
            // Shown as on page 2
            _search.ShownPositions[23] = 26;

            var teams = await Crawl();

            Assert.That(OtherRequests(), Is.EquivalentTo(new[] { _b.Url + "page-2", _b.Url + "post-23", "https://pokepast.es/b3" }));
            Assert.That(teams.Single(team => team.TeamString.TrimStart().StartsWith("MonB3")).URL, Is.EqualTo(_b.Url + "page-3#post-23"));
        }

        [Test]
        public async Task FullRefresh_WalksTheListing()
        {
            await FirstRun();

            await Crawl(new CollectorOptions { FullRefresh = true });

            Assert.That(SearchRequests(), Is.Empty);
            Assert.That(_http.Requests, Does.Contain(ForumUrl).And.Contain(_c.Url));
        }
    }
}
