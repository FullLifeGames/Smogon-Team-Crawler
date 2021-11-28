using NUnit.Framework;
using SmogonTeamCrawler.Core.Crawler;
using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;
using System.Linq;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class TestShowdownReplayScouter
    {
        private TeamCrawler _teamCrawler;

        [SetUp]
        public void SetUp()
        {
            _teamCrawler = new Core.Crawler.SmogonTeamCrawler();
        }

        [Test]
        public void Crawl_Scan()
        {
            var scanResult = _teamCrawler.Scanner.Scan().Result;

            Assert.IsTrue(scanResult.TierToRegularLinks.Any());
            Assert.IsTrue(scanResult.TierToRmtLinks.Any());
        }

        [Test]
        public void Crawl_Single()
        {
            var collectedTeams = new Dictionary<string, ICollection<Team>>();

            _teamCrawler.Collector.AnalyzeThread(
                collectedTeams,
                "gen8ou",
                "https://www.smogon.com/forums/threads/ss-ou-sample-teams-crown-tundra.3672556/",
                "gen8ou"
            ).Wait();
            Assert.IsTrue(collectedTeams.Any());

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.IsTrue(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.IsTrue(transformation.Any());
        }

        [Test]
        public void Crawl_Single_DPP()
        {
            var collectedTeams = new Dictionary<string, ICollection<Team>>();

            _teamCrawler.Collector.AnalyzeThread(
                collectedTeams,
                "gen4ou",
                "https://www.smogon.com/forums/threads/dpp-post-dugtrio-ban.3672744/",
                "gen4ou"
            ).Wait();
            Assert.IsTrue(collectedTeams.Any());

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.IsTrue(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.IsTrue(transformation.Any());
        }

        [Test]
        public void Crawl_Single_BDSP()
        {
            var scanResult = new Dictionary<string, string>() { { "BDSP Metagames", "https://www.smogon.com/forums/forums/bdsp-metagames.699/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.IsTrue(collectedTeams.Any());

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.IsTrue(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.IsTrue(transformation.Any());
            Assert.IsTrue(transformation.Keys.Any((key) => key.Contains("gen8bdsp")));
        }

        [Test]
        public void Crawl_Single_National_Dex()
        {
            var scanResult = new Dictionary<string, string>() { { "National Dex", "https://www.smogon.com/forums/forums/national-dex.533/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.IsTrue(collectedTeams.Any());

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.IsTrue(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.IsTrue(transformation.Any());
            Assert.IsTrue(transformation.Keys.Any((key) => key.Contains("gen8nationaldex")));
        }

        [Test]
        public void Crawl_Single_Lets_Go()
        {
            var scanResult = new Dictionary<string, string>() { { "SM", "https://www.smogon.com/forums/forums/sm/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.IsTrue(collectedTeams.Any());

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.IsTrue(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.IsTrue(transformation.Any());
            Assert.IsTrue(transformation.Keys.Any((key) => key.Contains("gen7letsgo")));
        }

        [Test]
        [Explicit]
        public void Crawl_Full()
        {
            var result = _teamCrawler.Crawl(new CrawlRequest()
            {
                MainForum = true,
                RMTForum = true,
            });

            Assert.IsTrue(result.SmogonTeams.Any());
            Assert.IsTrue(result.Rmts.Any());
            Assert.IsTrue(result.SmogonOutput.Length > 0);
            Assert.IsTrue(result.RmtsOutput.Length > 0);
            Assert.IsTrue(result.TeamsByTier.Any());
        }
    }
}
