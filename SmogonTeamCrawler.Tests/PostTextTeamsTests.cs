#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Collector;
using SmogonTeamCrawler.Core.Util;
using SmogonTeamCrawler.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class PostTextTeamsTests
    {
        /// <summary>Excerpts of Showdown's data files, by kind; the real ones are bigger.</summary>
        private const string Pokedex = """exports.BattlePokedex = {ninetales:{name:"Ninetales"},alakazam:{name:"Alakazam"},infernape:{name:"Infernape"},bronzong:{name:"Bronzong"},goodra:{name:"Goodra"},ditto:{name:"Ditto"},rotomwash:{name:"Rotom-Wash"}};""";
        private const string Moves = """exports.BattleMovedex = {flamethrower:{name:"Flamethrower"},solarbeam:{name:"Solar Beam"},sunnyday:{name:"Sunny Day"},psychic:{name:"Psychic"},focusblast:{name:"Focus Blast"},shadowball:{name:"Shadow Ball"},fakeout:{name:"Fake Out"},closecombat:{name:"Close Combat"},stealthrock:{name:"Stealth Rock"},uturn:{name:"U-turn"},reflect:{name:"Reflect"},hypnosis:{name:"Hypnosis"},lightscreen:{name:"Light Screen"},gyroball:{name:"Gyro Ball"},dracometeor:{name:"Draco Meteor"},fireblast:{name:"Fire Blast"},sludgebomb:{name:"Sludge Bomb"},toxic:{name:"Toxic"},transform:{name:"Transform"},voltswitch:{name:"Volt Switch"},hydropump:{name:"Hydro Pump"}};""";
        private const string Items = """exports.BattleItems = {leftovers:{name:"Leftovers"},focussash:{name:"Focus Sash"},lightclay:{name:"Light Clay"},choicescarf:{name:"Choice Scarf"}};""";
        private const string Abilities = """exports.BattleAbilities = {drought:{name:"Drought"},magicguard:{name:"Magic Guard"},blaze:{name:"Blaze"},levitate:{name:"Levitate"},sapsipper:{name:"Sap Sipper"},imposter:{name:"Imposter"}};""";
        private const string Aliases = """exports.BattleAliases = {rotomw:"Rotom-Wash",lefties:"Leftovers"};""";

        internal static ShowdownNames FixtureNames()
        {
            var names = new ShowdownNames();
            names.Add(Pokedex, ShowdownData.Species);
            names.Add(Moves, ShowdownData.Moves);
            names.Add(Items, ShowdownData.Items);
            names.Add(Abilities, ShowdownData.Abilities);
            names.Add(Aliases, ShowdownData.Aliases);
            return names;
        }

        /// <summary>Serves the data files of <see cref="FixtureNames"/> at Showdown's URLs.</summary>
        internal static void ServeShowdownData(FakeHttpHandler http)
        {
            string[] files = [Pokedex, Moves, Items, Abilities, Aliases];
            for (var i = 0; i < files.Length; i++)
            {
                http.Html(ShowdownNames.DataUrls[i], files[i]);
            }
        }

        /// <summary>Six sets the way posts of 2010 to 2016 wrote them, each in another spelling, with words in between.</summary>
        internal const string TypedTeam = """
            Here is my sun team, rate it please!<br />
            <br />
            <b>KURAMA</b><br />
            Ninetales (F) @ Leftovers<br />
            Trait: Drought<br />
            EVs: 252 SAtk / 40 HP / 216 Spd<br />
            Modest Nature (+SAtk, -Atk)<br />
            - Flamethrower<br />
            - Hidden Power [Ice]<br />
            - SolarBeam<br />
            - Sunny Day<br />
            Ninetales sets the sun, and Flamethrower hits hard in it.<br />
            <br />
            Uri (Alakazam) (M) @ Focus Sash<br />
            Ability: Magic Guard<br />
            Nature: Timid<br />
            EV spread: 252 SpAtk, 4 SpDef, 252 Spe<br />
            Moves:<br />
            1. Psychic<br />
            2. Focus Blast<br />
            3. Shadow Ball<br />
            4. HP Fire<br />
            <br />
            Infernape (Anti-Lead)<br />
            Blaze @ Focus Sash<br />
            EVs: 252 ATK / 252 SPD / 4 HP<br />
            Nature: Jolly<br />
            ~ Fake Out<br />
            ~ Close Combat<br />
            ~ Stealth Rock<br />
            ~ U-turn<br />
            <div>Bronzong @ Light Clay / Leftovers</div>
            Trait: Levitate | Nature: Relaxed<br />
            EVs: 252 HP / 160 Def / 96 SDef<br />
            - Reflect / Hypnosis<br />
            - Light Screen<br />
            - Gyro Ball<br />
            Gyro Ball hurts with its low Speed.<br />
            Goodra Leftovers<br />
            Ability: Sap Sipper<br />
            Nature: Calm<br />
            EVs: 252 HP / 4 Def / 252 SpD<br />
            - Draco Meteor<br />
            - Fire Blast<br />
            - Sludge Bomb<br />
            - Toxic<br />
            Ditto @ Choice Scarf<br />
            Trait: Imposter<br />
            EVs: 252 HP / 4 Def / 252 SDef<br />
            Sassy Nature<br />
            - Transform<br />
            Thanks for rating!
            """;

        /// <summary><see cref="TypedTeam"/> in Showdown's export format.</summary>
        internal const string TypedTeamExport = """
            Ninetales (F) @ Leftovers
            Ability: Drought
            EVs: 40 HP / 252 SpA / 216 Spe
            Modest Nature
            - Flamethrower
            - Hidden Power [Ice]
            - Solar Beam
            - Sunny Day

            Uri (Alakazam) (M) @ Focus Sash
            Ability: Magic Guard
            EVs: 252 SpA / 4 SpD / 252 Spe
            Timid Nature
            - Psychic
            - Focus Blast
            - Shadow Ball
            - Hidden Power [Fire]

            Infernape @ Focus Sash
            Ability: Blaze
            EVs: 4 HP / 252 Atk / 252 Spe
            Jolly Nature
            - Fake Out
            - Close Combat
            - Stealth Rock
            - U-turn

            Bronzong @ Light Clay
            Ability: Levitate
            EVs: 252 HP / 160 Def / 96 SpD
            Relaxed Nature
            - Reflect
            - Light Screen
            - Gyro Ball

            Goodra @ Leftovers
            Ability: Sap Sipper
            EVs: 252 HP / 4 Def / 252 SpD
            Calm Nature
            - Draco Meteor
            - Fire Blast
            - Sludge Bomb
            - Toxic

            Ditto @ Choice Scarf
            Ability: Imposter
            EVs: 252 HP / 4 Def / 252 SpD
            Sassy Nature
            - Transform

            """;

        private static string Post(string text) =>
            "<article class=\"message-body js-selectToQuote\">\n<div class=\"bbWrapper\">" + text + "</div>\n<div class=\"js-selectToQuoteEnd\">&nbsp;</div>\n</article>";

        [Test]
        public void TypedTeam_IsReadInShowdownsExportFormat()
        {
            var teams = PostTextTeams.Find(Post(TypedTeam), FixtureNames());

            Assert.That(teams, Has.Count.EqualTo(1));
            Assert.That(teams[0], Is.EqualTo(TypedTeamExport.ReplaceLineEndings("\n")));
        }

        [Test]
        public void TeamShownSetBySet_AndOnceMoreAsAWhole_IsOneTeam()
        {
            var teams = PostTextTeams.Find(Post(TypedTeam + "<br />\nImportable:<br />\n" + TypedTeam), FixtureNames());

            Assert.That(teams, Has.Count.EqualTo(1));
        }

        [Test]
        public void QuotedSets_AreLeftOut()
        {
            const string quote = "<blockquote class=\"bbCodeBlock bbCodeBlock--expandable bbCodeBlock--quote js-expandWatch\">\n"
                + "<div class=\"bbCodeBlock-content\">Rotom-W @ Lefties<br />\nAbility: Levitate<br />\nEVs: 252 HP / 252 Def / 4 SpD<br />\n"
                + "Bold Nature<br />\n- Volt Switch<br />\n- Hydro Pump<br />\n</div>\n</blockquote>\nNice team, but:<br />\n";

            var sets = PostTextTeams.FindSets(Post(quote + TypedTeam), FixtureNames());

            Assert.That(sets, Has.Count.EqualTo(6));
            Assert.That(sets[0], Does.StartWith("Ninetales"));
        }

        [Test]
        public void SpellingsOfOtherSites_AreShowdowns()
        {
            var sets = PostTextTeams.FindSets(Post("Spinny / Rotom-W ♂ @ Lefties<br />\nAbility: Levitate<br />\nBold Nature<br />\n- Volt Switch<br />\n- Hydro Pump"), FixtureNames());

            Assert.That(sets, Is.EqualTo(new[] { "Spinny (Rotom-Wash) (M) @ Leftovers\nAbility: Levitate\nBold Nature\n- Volt Switch\n- Hydro Pump" }));
        }

        [Test]
        public void FiveSets_AreNoTeam()
        {
            var fiveSets = TypedTeam[..TypedTeam.IndexOf("Ditto @")];

            Assert.That(PostTextTeams.FindSets(Post(fiveSets), FixtureNames()), Has.Count.EqualTo(5));
            Assert.That(PostTextTeams.Find(Post(fiveSets), FixtureNames()), Is.Empty);
        }

        [Test]
        public void SetsToChooseFrom_AreNoTeam()
        {
            // Three Ninetales sets: a post of sets to choose from
            var sets = TypedTeam
                .Replace("Uri (Alakazam) (M) @ Focus Sash", "Ninetales @ Focus Sash")
                .Replace("Infernape (Anti-Lead)", "Ninetales (Anti-Lead)");

            Assert.That(PostTextTeams.FindSets(Post(sets), FixtureNames()), Has.Count.EqualTo(6));
            Assert.That(PostTextTeams.Find(Post(sets), FixtureNames()), Is.Empty);
        }

        [Test]
        public void OneSpeciesTwice_IsATeam()
        {
            // Balanced Hackmons had no Species Clause ("Double Don": two Groudon).
            var sets = TypedTeam.Replace("Uri (Alakazam) (M) @ Focus Sash", "Ninetales @ Focus Sash");

            var teams = PostTextTeams.Find(Post(sets), FixtureNames());

            Assert.That(teams, Has.Count.EqualTo(1));
            Assert.That(teams[0], Does.Contain("\n\nNinetales @ Focus Sash\n"));
        }

        [Test]
        public void SetList_IsNoTeam()
        {
            // A set compendium: two Ninetales sets in a row, two Goodra sets in a row
            var sets = TypedTeam
                .Replace("Uri (Alakazam) (M) @ Focus Sash", SecondSet("Ninetales") + "Uri (Alakazam) (M) @ Focus Sash")
                .Replace("Ditto @ Choice Scarf", SecondSet("Goodra") + "Ditto @ Choice Scarf");

            Assert.That(PostTextTeams.FindSets(Post(sets), FixtureNames()), Has.Count.EqualTo(8));
            Assert.That(PostTextTeams.Find(Post(sets), FixtureNames()), Is.Empty);
        }

        private static string SecondSet(string species) =>
            $"{species} @ Choice Scarf<br />\nEVs: 252 SpA / 4 SpD / 252 Spe<br />\nTimid Nature<br />\n- Fire Blast<br />\n- Draco Meteor<br />\n<br />\n";

        [Test]
        public void SixSpeciesInAlphabeticalOrder_AreACutFromAList()
        {
            // Ninetales, Uri (Alakazam), Infernape, Bronzong, Goodra, Ditto
            var sets = TypedTeamExport.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string Html(IEnumerable<int> order) => Post(string.Join("<br />\n<br />\n", order.Select(i => sets[i].Replace("\n", "<br />\n"))));

            Assert.That(PostTextTeams.Find(Html([0, 1, 2, 3, 4, 5]), FixtureNames()), Has.Count.EqualTo(1));
            Assert.That(PostTextTeams.Find(Html([1, 3, 5, 4, 2, 0]), FixtureNames()), Is.Empty);
        }

        [TestCase("Ninetales @ Leftovers<br />\n- Flamethrower<br />\n- Sunny Day", TestName = "Moves without EVs, nature or ability")]
        [TestCase("Ninetales @ Leftovers<br />\nTrait: Drought<br />\nModest Nature", TestName = "No moves")]
        [TestCase("Ninetales is great with Leftovers.<br />\nModest Nature<br />\n- Flamethrower<br />\n- Sunny Day", TestName = "A species in a sentence")]
        public void Mentions_AreNoSets(string text)
        {
            Assert.That(PostTextTeams.FindSets(Post(text), FixtureNames()), Is.Empty);
        }

        [TestCase("252 SAtk / 4 SDef / 252 Spd", "252 SpA / 4 SpD / 252 Spe")]
        [TestCase("252 HP, 252 Def, 4 SpDef", "252 HP / 252 Def / 4 SpD")]
        [TestCase("4 HP / 252 Sp. Atk / 252 Speed", "4 HP / 252 SpA / 252 Spe")]
        [TestCase("252 Atk / 4 SpD / 252 Spe", "252 Atk / 4 SpD / 252 Spe")]
        [TestCase("252 Spe / 4 Special Defense / 252 Attack", "252 Atk / 4 SpD / 252 Spe")]
        public void Stats_AreShowdowns(string written, string expected)
        {
            Assert.That(PostTextTeams.ParseStats(written), Is.EqualTo(expected));
        }

        [Test]
        public void WithoutShowdownData_NothingIsRead()
        {
            Assert.That(PostTextTeams.Find(Post(TypedTeam), new ShowdownNames()), Is.Empty);
        }
    }
}
