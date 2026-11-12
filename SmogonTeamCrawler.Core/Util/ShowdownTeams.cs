using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Util
{
    /// <summary>
    /// Teams saved on Pokémon Showdown's team site. Posts link them as psim.us/t/ID, play.pokemonshowdown.com/t/ID or
    /// teams.pokemonshowdown.com/view/ID, private teams with "-password" after the ID. The site's API hands out the team
    /// in Showdown's packed format, which <see cref="Export"/> turns into the usual export text.
    /// </summary>
    public static partial class ShowdownTeams
    {
        [GeneratedRegex(@"(?:psim\.us/t/|play\.pokemonshowdown\.com/t/|teams\.pokemonshowdown\.com/view/)(\d+)(?:-([0-9A-Za-z]+))?")]
        private static partial Regex TeamLinkRegex();

        /// <summary>The distinct team links in a piece of HTML (a link usually shows up twice, in href and text).</summary>
        public static IReadOnlyList<(string Id, string? Password)> FindLinks(string html) => TeamLinkRegex().Matches(html)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null))
            .Distinct()
            .ToList();

        public static string ApiUrl(string id, string? password) =>
            "https://teams.pokemonshowdown.com/api/getteam?teamid=" + id
            + (password != null ? "&password=" + Uri.EscapeDataString(password) : "")
            + "&full=1";

        /// <summary>The team of a getteam answer as export text; "" if the team does not exist or is private.</summary>
        public static string FromApiResponse(string response, ShowdownNames names)
        {
            // The API prefixes its JSON with "]" against JSON hijacking.
            var json = response.TrimStart().TrimStart(']');
            try
            {
                return JObject.Parse(json)["team"] is JValue { Type: JTokenType.String } team ? Export((string)team!, names) : "";
            }
            catch (JsonException)
            {
                return "";
            }
        }

        private static readonly string[] StatNames = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];

        /// <summary>
        /// Packed format, one Pokémon per "]"-separated set:
        /// NICKNAME|SPECIES|ITEM|ABILITY|MOVES|NATURE|EVS|GENDER|IVS|SHINY|LEVEL|HAPPINESS,HPTYPE,POKEBALL,GIGANTAMAX,DYNAMAXLEVEL,TERATYPE
        /// (an empty species is the nickname; empty EVs are 0, empty IVs 31).
        /// </summary>
        public static string Export(string packed, ShowdownNames names)
        {
            var sets = new List<string>();
            foreach (var set in packed.Split(']'))
            {
                var fields = set.Split('|');
                string Field(int index) => index < fields.Length ? fields[index] : "";

                var nickname = Field(0);
                var species = Field(1).Length > 0 ? names.Get(Field(1)) : nickname;
                if (species.Length == 0)
                {
                    continue;
                }

                var lines = new List<string>();
                var title = nickname.Length > 0 && nickname != species ? $"{nickname} ({species})" : species;
                if (Field(7) is "M" or "F")
                {
                    title += $" ({Field(7)})";
                }
                if (Field(2).Length > 0)
                {
                    title += " @ " + names.Get(Field(2));
                }
                lines.Add(title);

                // "", "0", "1", "H" and "S" stand for an ability slot of the species.
                var ability = Field(3) is "" or "0" or "1" or "H" or "S"
                    ? names.SpeciesAbility(species, Field(3).Length > 0 ? Field(3) : "0")
                    : names.Get(Field(3));
                if (!string.IsNullOrEmpty(ability))
                {
                    lines.Add("Ability: " + ability);
                }
                if (Field(10).Length > 0 && Field(10) != "100")
                {
                    lines.Add("Level: " + Field(10));
                }
                if (Field(9).Length > 0)
                {
                    lines.Add("Shiny: Yes");
                }

                var misc = Field(11).Split(',');
                string Misc(int index) => index < misc.Length ? misc[index] : "";
                if (Misc(0).Length > 0 && Misc(0) != "255")
                {
                    lines.Add("Happiness: " + Misc(0));
                }
                if (Misc(2).Length > 0)
                {
                    lines.Add("Pokeball: " + names.Get(Misc(2)));
                }
                if (Misc(1).Length > 0)
                {
                    lines.Add("Hidden Power: " + Misc(1));
                }
                if (Misc(4).Length > 0 && Misc(4) != "10")
                {
                    lines.Add("Dynamax Level: " + Misc(4));
                }
                if (Misc(3).Length > 0)
                {
                    lines.Add("Gigantamax: Yes");
                }
                if (Misc(5).Length > 0)
                {
                    lines.Add("Tera Type: " + Misc(5));
                }

                var evs = Stats(Field(6), unset: 0, shownUnless: 0);
                if (evs.Length > 0)
                {
                    lines.Add("EVs: " + evs);
                }
                if (Field(5).Length > 0)
                {
                    lines.Add(char.ToUpperInvariant(Field(5)[0]) + Field(5)[1..] + " Nature");
                }
                var ivs = Stats(Field(8), unset: 31, shownUnless: 31);
                if (ivs.Length > 0)
                {
                    lines.Add("IVs: " + ivs);
                }
                foreach (var move in Field(4).Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    lines.Add("- " + names.Get(move));
                }
                sets.Add(string.Join("\n", lines));
            }
            return sets.Count == 0 ? "" : string.Join("\n\n", sets) + "\n";
        }

        private static string Stats(string packed, int unset, int shownUnless)
        {
            if (packed.Length == 0)
            {
                return "";
            }
            var values = packed.Split(',');
            var shown = new List<string>();
            for (var i = 0; i < StatNames.Length; i++)
            {
                var value = i < values.Length && int.TryParse(values[i], out var parsed) ? parsed : unset;
                if (value != shownUnless)
                {
                    shown.Add(value + " " + StatNames[i]);
                }
            }
            return string.Join(" / ", shown);
        }
    }

    /// <summary>
    /// Display names of species, moves, items and abilities from Showdown's data files, looked up by ID
    /// ("boosterenergy" is "Booster Energy"). Unknown IDs are spaced the way Showdown does it ("BoosterEnergy" to "Booster Energy").
    /// </summary>
    public partial class ShowdownNames
    {
        public static readonly string[] DataUrls =
        [
            "https://play.pokemonshowdown.com/data/pokedex.json",
            "https://play.pokemonshowdown.com/data/moves.json",
            "https://play.pokemonshowdown.com/data/items.js",
            "https://play.pokemonshowdown.com/data/abilities.js",
        ];

        private readonly Dictionary<string, string> _names = new();
        private readonly Dictionary<string, Dictionary<string, string>> _speciesAbilities = new();

        public int Count => _names.Count;

        /// <summary>Loads <see cref="DataUrls"/>; a file that cannot be loaded only costs nicer names.</summary>
        public static async Task<ShowdownNames> LoadAsync(HttpClient client)
        {
            var names = new ShowdownNames();
            foreach (var url in DataUrls)
            {
                try
                {
                    names.Add(await client.GetStringAsync(url).ConfigureAwait(false));
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Showdown data {url} not loaded ({e.Message}); its names are spelled from their IDs.");
                }
            }
            return names;
        }

        // name:"..." in the .js files, "name":"..." in the .json files
        [GeneratedRegex(@"(?:""name""|\bname)\s*:\s*""((?:[^""\\]|\\.)*)""")]
        private static partial Regex NameRegex();

        [GeneratedRegex("[^a-z0-9]")]
        private static partial Regex NonIdRegex();

        /// <summary>Takes the names of a data file (pokedex.json, moves.json, items.js, abilities.js).</summary>
        public void Add(string data)
        {
            foreach (Match match in NameRegex().Matches(data))
            {
                var name = match.Groups[1].Value;
                try
                {
                    // \uXXXX in the JSON files ("Flabébé")
                    name = Regex.Unescape(name);
                }
                catch (ArgumentException)
                {
                    // Keep it as written
                }
                _names.TryAdd(ToId(name), name);
                // Showdown packs names by dropping what is not A-Z or 0-9, so "Flabébé" arrives as "Flabb".
                _names.TryAdd(NonIdRegex().Replace(name.ToLowerInvariant(), ""), name);
            }
            if (data.TrimStart().StartsWith('{'))
            {
                AddSpeciesAbilities(data);
            }
        }

        private void AddSpeciesAbilities(string json)
        {
            try
            {
                foreach (var (_, entry) in JObject.Parse(json))
                {
                    if (entry?["name"] is JValue name && entry["abilities"] is JObject abilities)
                    {
                        _speciesAbilities.TryAdd(ToId((string)name!), abilities.Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? ""));
                    }
                }
            }
            catch (JsonException)
            {
                // Not the Pokédex
            }
        }

        public string Get(string idOrName) =>
            _names.TryGetValue(ToId(idOrName), out var name) ? name : Spell(idOrName);

        /// <summary>The ability in slot "0", "1", "H" or "S" of a species, if known.</summary>
        public string? SpeciesAbility(string species, string slot) =>
            _speciesAbilities.TryGetValue(ToId(species), out var abilities) && abilities.TryGetValue(slot, out var ability) ? ability : null;

        /// <summary>Lower case letters and digits; accents are dropped from their letters ("Flabébé" is "flabebe", as in the Pokédex).</summary>
        public static string ToId(string text)
        {
            var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            return NonIdRegex().Replace(decomposed, "");
        }

        /// <summary>Showdown's own fallback: a space before numbers and capitals.</summary>
        private static string Spell(string id)
        {
            var spaced = Regex.Replace(Regex.Replace(id, "([0-9]+)", " $1 "), "([A-Z])", " $1");
            return Regex.Replace(spaced, " {2,}", " ").Trim();
        }
    }
}
