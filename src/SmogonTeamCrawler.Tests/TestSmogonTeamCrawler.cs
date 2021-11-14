﻿using NUnit.Framework;
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
