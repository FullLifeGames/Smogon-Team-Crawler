#nullable enable
using NeoSmart.Caching.Sqlite;
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Tests
{
    /// <summary>The command line tool keeps the state in a SQLite file; this checks the state survives a restart.</summary>
    [TestFixture]
    public class SqliteStateTests
    {
        private string _directory = null!;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "stc-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // SQLite may still hold the file on Windows
            }
        }

        [Test]
        public async Task SecondRun_WithANewCacheInstance_OnlyReadsTheListing()
        {
            const string forumUrl = "https://www.smogon.com/forums/forums/overused.726/";
            var forum = new FakeForum(forumUrl);
            var start = new DateTime(2021, 3, 4, 5, 6, 0);
            for (var id = 1; id <= 50; id++)
            {
                var thread = forum.AddThread(id);
                thread.AddPage(new FakePost(id * 10, start.AddHours(id), "T" + id));
                if (id % 5 == 0)
                {
                    thread.AddPage(new FakePost(id * 10 + 1, start.AddHours(id).AddMinutes(1)));
                }
            }
            var http = new FakeHttpHandler();
            forum.Publish(http);
            var forums = new Dictionary<string, string> { ["gen9OverUsed"] = forumUrl };
            var cachePath = Path.Combine(_directory, "SmogonDump.db");

            async Task<int> Run()
            {
                http.Requests.Clear();
                using var cache = new SqliteCache(new SqliteCacheOptions { MemoryOnly = false, CachePath = cachePath });
                var collector = new TeamCollector(cache, new CollectorOptions { HttpClient = new HttpClient(http) });
                var collected = await collector.Collect(forums, false);
                return collected["gen9OverUsed"].Count;
            }

            var firstRunTeams = await Run();
            var firstRunRequests = http.Requests.Count;
            var secondRunTeams = await Run();

            Assert.That(firstRunTeams, Is.EqualTo(50));
            Assert.That(firstRunRequests, Is.EqualTo(2 + 50 + 10));
            Assert.That(secondRunTeams, Is.EqualTo(50));
            Assert.That(http.Requests, Is.EqualTo(new[] { forumUrl }));
        }
    }
}
