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

            Assert.That(scanResult.TierToRegularLinks.Count > 0);
            Assert.That(scanResult.TierToRmtLinks.Count > 0);
        }

        [Test]
        public void Crawl_Single()
        {
            var collectedTeams = new Dictionary<string, ICollection<Team>>();
            var result = _teamCrawler.Collector.AnalyzeThread(
                "https://www.smogon.com/forums/threads/ss-ou-sample-teams-crown-tundra.3672556/",
                "gen8ou"
            ).Result;
            collectedTeams.Add("gen8ou", result.CollectedTeams);

            Assert.That(collectedTeams.Count > 0);

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
        }

        [Test]
        public void Crawl_Single_DPP()
        {
            var collectedTeams = new Dictionary<string, ICollection<Team>>();
            var result = _teamCrawler.Collector.AnalyzeThread(
                "https://www.smogon.com/forums/threads/dpp-post-dugtrio-ban.3672744/",
                "gen4ou"
            ).Result;
            collectedTeams.Add("gen4ou", result.CollectedTeams);

            Assert.That(collectedTeams.Count > 0);

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
        }

        [Test]
        public void Crawl_Single_Date_Test()
        {
            var collectedTeams = new Dictionary<string, ICollection<Team>>();
            var result = _teamCrawler.Collector.AnalyzeThread(
                "https://www.smogon.com/forums/threads/sv-ou-metagame-discussion-v2-update-on-post-5186.3722192/",
                "gen9ou"
            ).Result;
            collectedTeams.Add("gen9ou", result.CollectedTeams);

            Assert.That(result.CollectedTeams.Count > 0);

            Assert.That(result.CollectedTeams.Any(team => team.PostedBy == "Delibird Heart" && team.PostDate < System.DateTime.Now.AddYears(-1)));

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
        }

        [Test]
        [Explicit]
        public void Crawl_Single_BDSP()
        {
            var scanResult = new Dictionary<string, string>() { { "BDSP Metagames", "https://www.smogon.com/forums/forums/bdsp-metagames.699/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.That(collectedTeams.Count > 0);

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
            Assert.That(transformation.Keys.Any((key) => key.Contains("gen8bdsp")));
        }

        [Test]
        [Explicit]
        public void Crawl_Single_National_Dex()
        {
            var scanResult = new Dictionary<string, string>() { { "National Dex", "https://www.smogon.com/forums/forums/national-dex.533/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.That(collectedTeams.Count > 0);

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
            Assert.That(transformation.Keys.Any((key) => key.Contains("gen8nationaldex")));
        }

        [Test]
        [Explicit]
        public void Crawl_Single_Lets_Go()
        {
            var scanResult = new Dictionary<string, string>() { { "SM", "https://www.smogon.com/forums/forums/sm/" } };

            var collectedTeams = _teamCrawler.Collector.Collect(scanResult, false).Result;
            Assert.That(collectedTeams.Count > 0);

            var output = _teamCrawler.Formatter.FormatOutput(collectedTeams);
            Assert.That(output.Length > 0);

            var transformation = _teamCrawler.Transformer.Transform(collectedTeams, new Dictionary<string, ICollection<Team>>());
            Assert.That(transformation.Count > 0);
            Assert.That(transformation.Keys.Any((key) => key.Contains("gen7letsgo")));
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

            Assert.That(result.TeamsByTier.Count > 0);
        }
    }
}
