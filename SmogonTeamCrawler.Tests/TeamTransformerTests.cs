using NUnit.Framework;
using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Transformer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class TeamTransformerTests
    {
        private const string Threads = "https://www.smogon.com/forums/threads/";

        private static string TierOf(string forumTier, string url, string teamTier = null)
        {
            var team = new Team("Garchomp @ Choice Scarf", 0, DateTime.Now, url, "Tester", "Resource") { TeamTier = teamTier };
            var byTier = new TeamTransformer().CreateTeamsByTiers(
                new Dictionary<string, ICollection<Team>> { [forumTier] = [team] },
                new Dictionary<string, ICollection<Team>>());
            return byTier.Single().Key;
        }

        // Pokémon Champions has its own formats on Showdown: "[Gen 9 Champions] OU" is gen9championsou.
        [TestCase("gen9Champions", Threads + "champions-ou-sample-teams.3783102/page-2#post-10770001", "[gen9championsou]")]
        [TestCase("gen9Champions", Threads + "champions-uu-metagame-discussion-october-shifts-131.3781741/", "[gen9championsuu]")]
        [TestCase("gen9Champions", Threads + "champions-uu-thank-you-for-playing.3790000/", "[gen9championsuu]")]
        [TestCase("gen9Champions", Threads + "post-champl-i-thread.3789589/", "[gen9championsou]")]
        [TestCase("gen9Champions Archive", Threads + "np-thunder-champions-ou-suspect-test.3788759/", "[gen9championsou]")]
        [TestCase("gen9Champions Archive", Threads + "mega-greninja.3780432/", "[gen9championsou]")]
        public void ChampionsForums_GetTheChampionsFormats(string forumTier, string url, string expected)
        {
            Assert.That(TierOf(forumTier, url), Is.EqualTo(expected));
        }

        [Test]
        public void ChampionsForums_KeepTheFormatOfAShowdownTeamLink()
        {
            Assert.That(TierOf("gen9Champions", Threads + "champions-ou-team-bazaar.3791153/", "gen9championsuu"), Is.EqualTo("[gen9championsuu]"));
        }

        [TestCase("gen9OverUsed", Threads + "smogon-champions-league-sv-ou-teams.3700000/", "[gen9ou]")]
        [TestCase("gen9UnderUsed", Threads + "uu-sample-teams.3710000/", "[gen9uu]")]
        public void OtherForums_KeepTheirFormats(string forumTier, string url, string expected)
        {
            Assert.That(TierOf(forumTier, url), Is.EqualTo(expected));
        }
    }
}
