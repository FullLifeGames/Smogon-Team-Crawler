using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Collector
{
    /// <summary>
    /// Teams typed into the text of a post, as in the Rate My Team threads before pastes took over (about 2010 to 2016),
    /// mostly in Pokémon Online's export: "Ninetales @ Leftovers", "Trait: Drought", "EVs: 252 HP / 120 SDef / 136 Spd",
    /// "Calm Nature (+SDef, -Atk)", moves with "-", "~" or nothing in front, with descriptions between the sets.
    /// Species, moves, items and abilities are checked against Showdown's data, so text is not taken for them.
    /// Six sets of six species in a row make a team, written in Showdown's export format.
    /// </summary>
    internal static partial class PostTextTeams
    {
        public const int SetsPerTeam = 6;

        private const int MaxMoves = 4;

        private static readonly string[] Natures =
        [
            "Adamant", "Bashful", "Bold", "Brave", "Calm", "Careful", "Docile", "Gentle", "Hardy", "Hasty", "Impish", "Jolly",
            "Lax", "Lonely", "Mild", "Modest", "Naive", "Naughty", "Quiet", "Quirky", "Rash", "Relaxed", "Sassy", "Serious", "Timid",
        ];

        private static readonly string[] Types =
        [
            "Bug", "Dark", "Dragon", "Electric", "Fairy", "Fighting", "Fire", "Flying", "Ghost", "Grass", "Ground", "Ice",
            "Poison", "Psychic", "Rock", "Steel", "Water",
        ];

        private static readonly string[] StatOrder = ["HP", "Atk", "Def", "SpA", "SpD", "Spe"];

        /// <summary>Whether a post is worth reading as text: typed sets name their EVs.</summary>
        public static bool MayHaveSets(string postHtml) => postHtml.Contains("EV", StringComparison.Ordinal);

        /// <summary>The teams typed into a post (its HTML, quotes are left out), in Showdown's export format.</summary>
        public static IReadOnlyList<string> Find(string postHtml, ShowdownNames names)
        {
            var teams = new List<string>();
            var team = new List<PokemonSet>();
            foreach (var set in ReadSets(postHtml, names))
            {
                var same = team.Count(member => member.Species == set.Species);
                if (same > 1 || (same == 1 && team.DistinctBy(member => member.Species).Count() < team.Count))
                {
                    // Sets to choose from ("Bouffalant @ Leftovers", "Bouffalant @ Choice Band", …), or the next team.
                    // One species twice is still a team: Balanced Hackmons had no Species Clause.
                    team.Clear();
                }
                team.Add(set);
                if (team.Count == SetsPerTeam)
                {
                    teams.Add(string.Join("\n\n", team.Select(member => member.Export())) + "\n");
                    team.Clear();
                }
            }
            return teams;
        }

        /// <summary>The sets typed into a post, each once, in Showdown's export format.</summary>
        internal static List<string> FindSets(string postHtml, ShowdownNames names) =>
            ReadSets(postHtml, names).Select(set => set.Export()).ToList();

        private static List<PokemonSet> ReadSets(string postHtml, ShowdownNames names)
        {
            var sets = new List<PokemonSet>();
            PokemonSet? current = null;
            var lines = TextLines(postHtml).SelectMany(SplitDetails).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (ParseHeader(line, i + 1 < lines.Count ? lines[i + 1] : null, names) is { } header)
                {
                    Finish(current, sets);
                    current = header;
                }
                else if (current == null || LabelRegex().IsMatch(line) || current.TryAddDetail(line, names))
                {
                    continue;
                }
                else if (current.Moves.Count < MaxMoves && ParseMove(line, names) is { } move)
                {
                    current.Moves.Add(move);
                }
                else if (current.Moves.Count == 0 && ParseMoveList(line, names) is { } moves)
                {
                    current.Moves.AddRange(moves);
                }
                else
                {
                    // The description of the set
                    Finish(current, sets);
                    current = null;
                }
            }
            Finish(current, sets);

            // A team shown set by set and then once more as a whole is one team.
            return sets.DistinctBy(set => set.Key).ToList();
        }

        private static void Finish(PokemonSet? set, List<PokemonSet> sets)
        {
            if (set != null && set.IsComplete)
            {
                sets.Add(set);
            }
        }

        /// <summary>The lines of text of a post: line breaks and blocks end a line, quotes are dropped, entities decoded.</summary>
        internal static List<string> TextLines(string postHtml)
        {
            var start = postHtml.IndexOf("<div class=\"bbWrapper\">", StringComparison.Ordinal);
            var end = postHtml.IndexOf("<div class=\"js-selectToQuoteEnd\">", Math.Max(0, start), StringComparison.Ordinal);
            var html = postHtml[Math.Max(0, start)..(end > 0 ? end : postHtml.Length)];
            html = WithoutQuotes(html);
            html = LineBreakRegex().Replace(html, "\n");
            html = TagRegex().Replace(html, "");
            return WebUtility.HtmlDecode(html)
                .Split('\n')
                .Select(line => RichTextStyleRegex().Replace(line.Replace(' ', ' '), "").Trim())
                .Where(line => line.Length > 0)
                .ToList();
        }

        /// <summary>Quotes (nested ones too) repeat what others posted.</summary>
        private static string WithoutQuotes(string html)
        {
            var result = new StringBuilder();
            var depth = 0;
            var position = 0;
            while (position < html.Length)
            {
                var open = html.IndexOf("<blockquote", position, StringComparison.Ordinal);
                var close = html.IndexOf("</blockquote>", position, StringComparison.Ordinal);
                if (open < 0 && close < 0)
                {
                    if (depth == 0)
                    {
                        result.Append(html, position, html.Length - position);
                    }
                    break;
                }
                if (open >= 0 && (close < 0 || open < close))
                {
                    if (depth == 0)
                    {
                        result.Append(html, position, open - position);
                    }
                    depth++;
                    position = open + "<blockquote".Length;
                }
                else
                {
                    if (depth == 0)
                    {
                        // A closing tag without opening one
                        result.Append(html, position, close - position);
                    }
                    depth = Math.Max(0, depth - 1);
                    position = close + "</blockquote>".Length;
                    result.Append('\n');
                }
            }
            return result.ToString();
        }

        // Spoilers are <details><summary>Title</summary><div>…
        [GeneratedRegex(@"<br\s*/?>|</?(?:div|p|li|ul|ol|h[1-6]|tr|td|th|table|details|summary|button|pre|hr)\b[^>]*>", RegexOptions.IgnoreCase)]
        private static partial Regex LineBreakRegex();

        [GeneratedRegex("<[^>]*>")]
        private static partial Regex TagRegex();

        // Pokémon Online copied its export as rich text, and posts kept the style rule as text.
        [GeneratedRegex(@"^\s*p,\s*li\s*\{[^}]*\}")]
        private static partial Regex RichTextStyleRegex();

        // "Moves:", "EVs and Nature:", "Attacks"
        [GeneratedRegex(@"^(?:[A-Za-z][A-Za-z &/']{0,24}:|Moves?|Moveset|Attacks)$", RegexOptions.IgnoreCase)]
        private static partial Regex LabelRegex();

        [GeneratedRegex(@"^(?:Item|Held Item|Ability|Trait|Nature|EVs?|IVs?)\b", RegexOptions.IgnoreCase)]
        private static partial Regex DetailStartRegex();

        // Before the next label of "Trait: Teravolt | Nature: Hasty" and "Item: Flying Gem	Ability: Prankster"
        [GeneratedRegex(@"\s*[|,;]?\s*(?=\b(?:Item|Ability|Trait|Nature|EVs?|IVs?)\s*:)", RegexOptions.IgnoreCase)]
        private static partial Regex NextDetailRegex();

        /// <summary>Lines with several details, split into one line per detail.</summary>
        private static IEnumerable<string> SplitDetails(string line) => DetailStartRegex().IsMatch(line)
            ? NextDetailRegex().Split(line).Select(part => part.Trim()).Where(part => part.Length > 0)
            : [line];

        // "1. ", "1) ", "#1 ", "1 - "
        [GeneratedRegex(@"^#?\d{1,2}\s*[.):\-]\s+")]
        private static partial Regex NumberingRegex();

        [GeneratedRegex(@"\s*(?:\((M|F)\)|(♂|♀))")]
        private static partial Regex GenderRegex();

        [GeneratedRegex(@"^(.*?)\s*\(([^()]+)\)$")]
        private static partial Regex NicknameRegex();

        /// <summary>
        /// "Species @ Item", "Nickname (Species) (M) @ Item", "Species (role) @ Item", "Nickname / Species ♂"; a line with
        /// only the species when the set follows ("Tyranitar", then "Item: Choice Band", "Blaze @ Focus Sash" or "- Crunch").
        /// </summary>
        private static PokemonSet? ParseHeader(string line, string? nextLine, ShowdownNames names)
        {
            line = NumberingRegex().Replace(line, "");
            var left = line;
            string? item = null;
            var at = line.IndexOf('@');
            if (at >= 0)
            {
                left = line[..at].Trim();
                item = ParseItem(line[(at + 1)..], names);
            }
            else if (nextLine == null || !StartsSet(nextLine, names))
            {
                return null;
            }

            string? gender = null;
            var genderMatch = GenderRegex().Match(left);
            if (genderMatch.Success)
            {
                gender = genderMatch.Groups[1].Success ? genderMatch.Groups[1].Value : genderMatch.Groups[2].Value == "♂" ? "M" : "F";
                // "Mamoswine (F) (Peaches)"
                left = left.Remove(genderMatch.Index, genderMatch.Length).Trim();
            }
            var nicknameMatch = NicknameRegex().Match(left);
            var found = nicknameMatch.Success && names.Find(ShowdownData.Species, nicknameMatch.Groups[2].Value) is { } named
                ? (named, nicknameMatch.Groups[1].Value.Trim())
                // In brackets the role ("Infernape (Anti-Lead)")
                : (nicknameMatch.Success ? FindSpecies(nicknameMatch.Groups[1].Value, names) : null) ?? FindSpecies(left, names);
            if (found == null && at < 0 && SpeciesAndItem(left, names) is { } speciesAndItem)
            {
                // "Goodra Leftovers"
                found = (speciesAndItem.Species, null);
                item = speciesAndItem.Item;
            }
            if (found is not { } result)
            {
                return null;
            }
            var (species, nickname) = result;
            if (nickname?.Length == 0 || nickname == species)
            {
                nickname = null;
            }
            return new PokemonSet(species, nickname, gender, item);
        }

        /// <summary>"Species" or "Nickname / Species".</summary>
        private static (string Species, string? Nickname)? FindSpecies(string text, ShowdownNames names)
        {
            if (names.Find(ShowdownData.Species, text) is { } species)
            {
                return (species, null);
            }
            var slash = text.LastIndexOf('/');
            if (slash > 0 && names.Find(ShowdownData.Species, text[(slash + 1)..]) is { } named)
            {
                return (named, text[..slash].Trim());
            }
            return null;
        }

        /// <summary>The two names of "Goodra Leftovers", if both are known.</summary>
        private static (string Species, string Item)? SpeciesAndItem(string text, ShowdownNames names) =>
            SplitInTwo(text, ShowdownData.Species, ShowdownData.Items, names);

        /// <summary>
        /// Text that is two known names in a row, without "/" between them ("Seed Bomb Leech Seed", "Leftovers Assault Vest":
        /// one was struck through or coloured as the alternative).
        /// </summary>
        private static (string First, string Second)? SplitInTwo(string text, ShowdownData first, ShowdownData second, ShowdownNames names)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 8)
            {
                // A sentence; names have up to four words
                return null;
            }
            for (var cut = 1; cut < words.Length; cut++)
            {
                if (names.Find(first, string.Join(' ', words[..cut])) is { } one && names.Find(second, string.Join(' ', words[cut..])) is { } other)
                {
                    return (one, other);
                }
            }
            return null;
        }

        /// <summary>Whether a line after a species alone shows that its set follows.</summary>
        private static bool StartsSet(string line, ShowdownNames names) =>
            IsDetail(line) || AbilityAtItem(line, names) != null || (MovePrefixRegex().IsMatch(line) && ParseMove(line, names) != null);

        /// <summary>"Blaze @ Focus Sash" after the species.</summary>
        private static string? AbilityAtItem(string line, ShowdownNames names)
        {
            var at = line.IndexOf('@');
            return at > 0 ? names.Find(ShowdownData.Abilities, line[..at]) : null;
        }

        [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
        private static partial Regex TrailingRemarkRegex();

        [GeneratedRegex(@"^[A-Za-z][A-Za-z .'\-]{1,28}$")]
        private static partial Regex NameLikeRegex();

        /// <summary>The item (the first of "Light Clay / Leftovers"), by Showdown's name if it knows it.</summary>
        private static string? ParseItem(string text, ShowdownNames names) => ParseName(text, ShowdownData.Items, names);

        /// <summary>The first of the names written ("Leftovers / Life Orb", "Levitate (Heatproof is …)"), as Showdown spells it.</summary>
        private static string? ParseName(string text, ShowdownData kind, ShowdownNames names)
        {
            text = TrailingRemarkRegex().Replace(text.Split('/')[0].Trim(), "").Trim();
            if ((names.Find(kind, text) ?? SplitInTwo(text, kind, kind, names)?.First) is { } name)
            {
                return name;
            }
            return NameLikeRegex().IsMatch(text) ? text : null;
        }

        [GeneratedRegex(@"^(?:Ability|Trait|Abilities)\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex AbilityRegex();

        // "Item: Leftovers", "Item Attached: Life Orb"
        [GeneratedRegex(@"^(?:Held\s+)?Item\b[^:]{0,12}:\s*(.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex ItemRegex();

        // "EVs: 252 HP / …", "EV Spread: 252 HP, …", "EVs 252 HP/…"
        [GeneratedRegex(@"^EV'?s?(?:\s+(?:spread|distribution))?\s*[:=\-]?\s*(\d.*)$", RegexOptions.IgnoreCase)]
        private static partial Regex EvsRegex();

        [GeneratedRegex(@"^IV'?s?(?:\s+(?:spread|distribution))?\s*[:=\-]?\s*(\d.*)$", RegexOptions.IgnoreCase)]
        private static partial Regex IvsRegex();

        // "Calm Nature (+SDef, -Atk)", "Nature: Adamant", "Jolly (+Spe, -SpA)", "Timid", "Timid-nature"
        [GeneratedRegex(@"^(?:Nature\s*[:\-]\s*)?([A-Za-z]+)(?:[\s\-]+Nature)?\s*(?:\(.*\))?\.?$", RegexOptions.IgnoreCase)]
        private static partial Regex NatureRegex();

        // "… / 252 Spd Timid Nature (+Spd, -Atk)"
        [GeneratedRegex(@"\b([A-Za-z]+)\s+Nature\b", RegexOptions.IgnoreCase)]
        private static partial Regex NatureInTextRegex();

        [GeneratedRegex(@"^Level\s*:\s*(\d{1,3})$", RegexOptions.IgnoreCase)]
        private static partial Regex LevelRegex();

        [GeneratedRegex(@"^Shiny\s*:\s*Yes$", RegexOptions.IgnoreCase)]
        private static partial Regex ShinyRegex();

        [GeneratedRegex(@"^Happiness\s*:\s*(\d{1,3})$", RegexOptions.IgnoreCase)]
        private static partial Regex HappinessRegex();

        private static bool IsDetail(string line) =>
            AbilityRegex().IsMatch(line) || ItemRegex().IsMatch(line) || EvsRegex().IsMatch(line) || ParseNature(line) != null;

        private static string? ParseNature(string line)
        {
            // The first of "Adamant Nature / Jolly Nature"
            var match = NatureRegex().Match(line.Split('/')[0].Trim());
            return match.Success ? NatureNamed(match.Groups[1].Value) : null;
        }

        private static string? NatureNamed(string word) =>
            Natures.FirstOrDefault(nature => nature.Equals(word, StringComparison.OrdinalIgnoreCase));

        // A number and one stat: "252 SAtk", "4 Sp. Def", "252 Special Attack", "6 HP"
        [GeneratedRegex(@"(\d{1,3})\s*((?:Sp(?:ecial|c)?\.?\s*)?[A-Za-z]+\.?)")]
        private static partial Regex StatRegex();

        /// <summary>"252 SAtk / 4 SDef / 252 Spd" as "252 SpA / 4 SpD / 252 Spe"; null if no stat is known.</summary>
        internal static string? ParseStats(string text)
        {
            var values = new Dictionary<string, int>();
            foreach (Match match in StatRegex().Matches(text))
            {
                if (StatName(match.Groups[2].Value) is { } stat && int.TryParse(match.Groups[1].Value, out var value))
                {
                    values.TryAdd(stat, value);
                }
            }
            return values.Count == 0
                ? null
                : string.Join(" / ", StatOrder.Where(values.ContainsKey).Select(stat => values[stat] + " " + stat));
        }

        /// <summary>
        /// Showdown's name of a stat. "Spd" is Speed in Pokémon Online's export and most old posts; only "SpD" written
        /// like that is Special Defense, as in Showdown.
        /// </summary>
        private static string? StatName(string label)
        {
            var written = label.Trim().TrimEnd('.').Trim();
            if (written == "SpD")
            {
                return "SpD";
            }
            return written.ToLowerInvariant().Replace(".", "").Replace(" ", "") switch
            {
                "hp" or "health" => "HP",
                "atk" or "att" or "attack" => "Atk",
                "def" or "defense" or "defence" => "Def",
                "spa" or "spatk" or "spatt" or "satk" or "satt" or "sa" or "spattack" or "specialattack" or "spcatk" or "spat" => "SpA",
                "spdef" or "sdef" or "sd" or "spdefense" or "spdefence" or "specialdefense" or "specialdefence" or "spcdef" => "SpD",
                "spd" or "spe" or "speed" or "spee" => "Spe",
                _ => null,
            };
        }

        [GeneratedRegex(@"^(?:[-~•*>–—·+]+\s*|\d\s*[.)]\s*)")]
        private static partial Regex MovePrefixRegex();

        [GeneratedRegex(@"^(?:Hidden Power|HP)\s*[\[(]?\s*([A-Za-z]+)\s*[\])]?$", RegexOptions.IgnoreCase)]
        private static partial Regex HiddenPowerRegex();

        /// <summary>A move line ("- Surf", "~ Fake Out", "Hidden Power [Ice]", the first of "Reflect / Hypnosis").</summary>
        private static string? ParseMove(string line, ShowdownNames names)
        {
            var text = MovePrefixRegex().Replace(line, "");
            text = TrailingRemarkRegex().Replace(text.Split('/')[0].Trim(), "").Trim();
            var hiddenPower = HiddenPowerRegex().Match(text);
            if (hiddenPower.Success && Types.FirstOrDefault(type => type.Equals(hiddenPower.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) is { } type)
            {
                return "Hidden Power [" + type + "]";
            }
            return names.Find(ShowdownData.Moves, text) ?? SplitInTwo(text, ShowdownData.Moves, ShowdownData.Moves, names)?.First;
        }

        /// <summary>The moves of "Substitute | Earth Power | Fusion Bolt | Ice Beam", if each is one.</summary>
        private static List<string>? ParseMoveList(string line, ShowdownNames names)
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > MaxMoves)
            {
                return null;
            }
            var moves = new List<string>();
            foreach (var part in parts)
            {
                if (ParseMove(part, names) is not { } move)
                {
                    return null;
                }
                moves.Add(move);
            }
            return moves;
        }

        private sealed class PokemonSet(string species, string? nickname, string? gender, string? item)
        {
            public string Species => species;
            public List<string> Moves { get; } = new();
            private string? _item = item;
            private string? _ability;
            private string? _evs;
            private string? _ivs;
            private string? _nature;
            private string? _level;
            private string? _happiness;
            private bool _shiny;

            /// <summary>Its species and moves: the same set shown twice in a post.</summary>
            public string Key => species + "|" + string.Join(",", Moves);

            /// <summary>
            /// A set, not a mention: two moves and EVs, nature or ability. One move is enough with EVs and nature or ability
            /// (Ditto has only Transform).
            /// </summary>
            public bool IsComplete =>
                (Moves.Count >= 2 && (_evs != null || _nature != null || _ability != null))
                || (Moves.Count == 1 && _evs != null && (_nature != null || _ability != null));

            /// <summary>
            /// Takes an ability, item, EVs, IVs, nature, level, shininess or happiness. False for what is no detail, and for
            /// one the set has already: that starts the next set, whose header could not be read.
            /// </summary>
            public bool TryAddDetail(string line, ShowdownNames names)
            {
                if (Moves.Count > 0 && Moves.Count < MaxMoves && ParseMove(line, names) != null)
                {
                    // Moves come last; a move named like a detail is a move.
                    return false;
                }
                if (AbilityRegex().Match(line) is { Success: true } ability)
                {
                    return Take(ref _ability, ParseName(ability.Groups[1].Value, ShowdownData.Abilities, names));
                }
                if (AbilityAtItem(line, names) is { } named)
                {
                    var at = line.IndexOf('@');
                    _item ??= ParseItem(line[(at + 1)..], names);
                    return Take(ref _ability, named);
                }
                if (ItemRegex().Match(line) is { Success: true } item)
                {
                    return Take(ref _item, ParseItem(item.Groups[1].Value, names));
                }
                if (EvsRegex().Match(line) is { Success: true } evs)
                {
                    if (NatureInTextRegex().Match(evs.Groups[1].Value) is { Success: true } nature && _evs == null)
                    {
                        _nature ??= NatureNamed(nature.Groups[1].Value);
                    }
                    return Take(ref _evs, ParseStats(evs.Groups[1].Value));
                }
                if (IvsRegex().Match(line) is { Success: true } ivs)
                {
                    return Take(ref _ivs, ParseStats(ivs.Groups[1].Value));
                }
                if (ParseNature(line) is { } natureName)
                {
                    return Take(ref _nature, natureName);
                }
                if (LevelRegex().Match(line) is { Success: true } level)
                {
                    return Take(ref _level, level.Groups[1].Value);
                }
                if (HappinessRegex().Match(line) is { Success: true } happiness)
                {
                    return Take(ref _happiness, happiness.Groups[1].Value);
                }
                if (ShinyRegex().IsMatch(line) && !_shiny)
                {
                    _shiny = true;
                    return true;
                }
                return false;
            }

            /// <summary>A detail line: taken if the set has none of its kind yet; one it cannot read is passed over.</summary>
            private static bool Take(ref string? field, string? value)
            {
                if (field != null)
                {
                    return false;
                }
                field = value;
                return true;
            }

            public string Export()
            {
                var title = nickname != null ? $"{nickname} ({species})" : species;
                if (gender != null)
                {
                    title += $" ({gender})";
                }
                if (_item != null)
                {
                    title += " @ " + _item;
                }
                var lines = new List<string> { title };
                if (_ability != null)
                {
                    lines.Add("Ability: " + _ability);
                }
                if (_level != null && _level != "100")
                {
                    lines.Add("Level: " + _level);
                }
                if (_shiny)
                {
                    lines.Add("Shiny: Yes");
                }
                if (_happiness != null && _happiness != "255")
                {
                    lines.Add("Happiness: " + _happiness);
                }
                if (_evs != null)
                {
                    lines.Add("EVs: " + _evs);
                }
                if (_nature != null)
                {
                    lines.Add(_nature + " Nature");
                }
                if (_ivs != null)
                {
                    lines.Add("IVs: " + _ivs);
                }
                lines.AddRange(Moves.Select(move => "- " + move));
                return string.Join("\n", lines);
            }
        }
    }
}
