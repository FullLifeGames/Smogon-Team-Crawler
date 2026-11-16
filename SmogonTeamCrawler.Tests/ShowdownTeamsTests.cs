#nullable enable
using NUnit.Framework;
using SmogonTeamCrawler.Core.Util;

namespace SmogonTeamCrawler.Tests
{
    [TestFixture]
    public class ShowdownTeamsTests
    {
        /// <summary>Six Pokémon in Showdown's packed format, as the team site's API returns them.</summary>
        public const string PackedTeam =
            "Roaring Moon||BoosterEnergy|protosynthesis|DragonDance,Earthquake,KnockOff,Acrobatics|adamant|,252,,,4,252|||||,,,,,Flying]"
            + "Moony|GreatTusk|HeavyDutyBoots|protosynthesis|RapidSpin,HeadlongRush,KnockOff,IceSpinner|jolly|252,4,,,,252|F||S|50|,,,,,Ground]"
            + "Darkrai||Leftovers|baddreams|NastyPlot,DarkPulse,SludgeBomb,FocusBlast|timid|4,,,252,,252||,0,,,,|||,,,,,Poison]"
            + "Glimmora||RedCard|toxicdebris|StealthRock,MortalSpin,EarthPower,PowerGem|bold|252,,252,,4,|||||,,,,,Ghost]"
            + "Zapdos||ChoiceSpecs|static|VoltSwitch,Hurricane,Discharge,HeatWave|modest|,,,252,4,252|||||,,,,,Electric]"
            + "Kingambit||BlackGlasses|supremeoverlord|SwordsDance,KowtowCleave,SuckerPunch,IronHead|adamant|252,252,,,4,|||||,,,,,Dark";

        /// <summary>Excerpts of Showdown's data files in their real shapes (JSON for pokedex/moves, JS for items/abilities).</summary>
        public static ShowdownNames FixtureNames()
        {
            var names = new ShowdownNames();
            names.Add("""{"roaringmoon":{"num":1005,"name":"Roaring Moon","abilities":{"0":"Protosynthesis"}},"greattusk":{"name":"Great Tusk","abilities":{"0":"Protosynthesis"}},"bulbasaur":{"name":"Bulbasaur","abilities":{"0":"Overgrow","H":"Chlorophyll"}},"flabebe":{"name":"Flabébé","abilities":{"0":"Flower Veil"}}}""");
            names.Add("""{"dragondance":{"num":349,"name":"Dragon Dance"},"knockoff":{"name":"Knock Off"},"uturn":{"name":"U-turn"}}""");
            names.Add("""exports.BattleItems = {boosterenergy:{name:"Booster Energy",spritenum:745,num:1880},heavydutyboots:{name:"Heavy-Duty Boots",num:1120}};""");
            names.Add("""exports.BattleAbilities = {protosynthesis:{flags:{},name:"Protosynthesis",rating:3},toxicdebris:{flags:{},name:"Toxic Debris"}};""");
            return names;
        }

        [Test]
        public void Export_WritesShowdownsExportFormat()
        {
            var export = ShowdownTeams.Export(PackedTeam, FixtureNames());
            var sets = export.Split("\n\n");

            Assert.That(sets, Has.Length.EqualTo(6));
            Assert.That(sets[0], Is.EqualTo("""
                Roaring Moon @ Booster Energy
                Ability: Protosynthesis
                Tera Type: Flying
                EVs: 252 Atk / 4 SpD / 252 Spe
                Adamant Nature
                - Dragon Dance
                - Earthquake
                - Knock Off
                - Acrobatics
                """.ReplaceLineEndings("\n")));
            Assert.That(sets[1], Does.StartWith("""
                Moony (Great Tusk) (F) @ Heavy-Duty Boots
                Ability: Protosynthesis
                Level: 50
                Shiny: Yes
                Tera Type: Ground
                EVs: 252 HP / 4 Atk / 252 Spe
                Jolly Nature
                - Rapid Spin
                """.ReplaceLineEndings("\n")));
            Assert.That(sets[2], Does.Contain("Timid Nature\nIVs: 0 Atk\n- Nasty Plot"));
            Assert.That(sets[3], Does.Contain("Ability: Toxic Debris"));
            Assert.That(export, Does.EndWith("- Iron Head\n"));
        }

        [Test]
        public void Export_WithoutData_SpellsNamesFromTheirIds()
        {
            var sets = ShowdownTeams.Export(PackedTeam, new ShowdownNames()).Split("\n\n");

            // Showdown imports these as well; only the abilities stay lower case IDs.
            Assert.That(sets[0], Does.StartWith("Roaring Moon @ Booster Energy\nAbility: Protosynthesis\n").Or.StartWith("Roaring Moon @ Booster Energy\nAbility: protosynthesis\n"));
            Assert.That(sets[1], Does.StartWith("Moony (Great Tusk) (F) @ Heavy Duty Boots"));
            Assert.That(sets[0], Does.Contain("- Knock Off"));
        }

        [Test]
        public void Export_ResolvesAbilitySlots()
        {
            var export = ShowdownTeams.Export("Bulbasaur||Leftovers|H|Growth|modest||||||", FixtureNames());

            Assert.That(export, Does.StartWith("Bulbasaur @ Leftovers\nAbility: Chlorophyll\n"));
        }

        [TestCase("flabebe")]
        [TestCase("Flabb")]
        public void Names_KeepAccents(string packed)
        {
            Assert.That(FixtureNames().Get(packed), Is.EqualTo("Flabébé"));
        }

        [Test]
        public void Names_DecodeUnicodeEscapes()
        {
            var names = new ShowdownNames();
            var escaped = @"\" + "u00e9";
            names.Add("{\"flabebe\":{\"name\":\"Flab" + escaped + "b" + escaped + "\"}}");

            Assert.That(names.Get("flabebe"), Is.EqualTo("Flabébé"));
        }

        [Test]
        public void FindLinks_FindsEveryFormOnce()
        {
            var html = """<a href="https://psim.us/t/2075556" class="link">https://psim.us/t/2075556</a> and """
                + """<a href="https://teams.pokemonshowdown.com/view/123-Ab9">private</a>, play.pokemonshowdown.com/t/77, """
                + """not a team: https://replay.pokemonshowdown.com/gen9ou-1""";

            Assert.That(ShowdownTeams.FindLinks(html), Is.EqualTo(new (string, string?)[] { ("2075556", null), ("123", "Ab9"), ("77", null) }));
        }

        [Test]
        public void ApiUrl_CarriesThePassword()
        {
            Assert.That(ShowdownTeams.ApiUrl("123", "Ab9"), Is.EqualTo("https://teams.pokemonshowdown.com/api/getteam?teamid=123&password=Ab9&full=1"));
            Assert.That(ShowdownTeams.ApiUrl("123", null), Is.EqualTo("https://teams.pokemonshowdown.com/api/getteam?teamid=123&full=1"));
        }

        [TestCase("]{\"team\":null}")]
        [TestCase("]{\"actionerror\":\"Team not found.\"}")]
        [TestCase("<html>error</html>")]
        public void FromApiResponse_WithoutTeam_IsEmpty(string response)
        {
            Assert.That(ShowdownTeams.FromApiResponse(response, FixtureNames()), Is.Empty);
        }

        [Test]
        public void FromApiResponse_ExportsTheTeam()
        {
            var response = "]" + Newtonsoft.Json.JsonConvert.SerializeObject(new { teamid = "1", team = PackedTeam, format = "gen9ou" });

            Assert.That(ShowdownTeams.FromApiResponse(response, FixtureNames()), Does.StartWith("Roaring Moon @ Booster Energy\n"));
        }
    }
}
