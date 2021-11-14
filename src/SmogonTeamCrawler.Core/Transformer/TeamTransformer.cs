using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmogonTeamCrawler.Core.Transformer
{
    public class TeamTransformer : ITransformer
    {
        private static readonly Dictionary<string, string> _mapping = new Dictionary<string, string>
        {
            { "ss", "gen8" },
            { "sm", "gen7" },
            { "oras", "gen6" },
            { "xy", "gen6" },
            { "bw", "gen5" },
            { "bw2", "gen5" },
            { "dpp", "gen4" },
            { "adv", "gen3" },
            { "adv.", "gen3" },
            { "gsc", "gen2" },
            { "rby", "gen1" },
            { "stadium", "gen1" },
        };
        private static readonly Dictionary<string, string> _mappingWithSpace = _mapping.Select((val) => new KeyValuePair<string, string>(val.Key + " ", val.Value))
                                                                               .ToDictionary(x => x.Key, x => x.Value);

        private static readonly List<string> _listOfTiers = new List<string>
        {
            "ou",
            "uu",
            "ru",
            "nu",
            "pu",
            "zu",
            "ubers",
            "doubles"
        };
        private static readonly List<string> _listOfTiersWithSpace = _listOfTiers.Select((val) => val + " ").ToList();

        public Dictionary<string, string> Transform(IDictionary<string, ICollection<Team>> smogonTeams, IDictionary<string, ICollection<Team>> rmts)
        {
            var teamByActualTiers = new Dictionary<string, List<Team>>();

            foreach (var tier in smogonTeams)
            {
                var tierDef = tier.Key;
                foreach (var team in tier.Value)
                {
                    var showdownTier = TranslateSmogonTeamsTier(tierDef, team.URL, team.TeamTag);
                    if (showdownTier == null)
                    {
                        showdownTier = "";
                    }
                    else
                    {
                        showdownTier = "[" + showdownTier + "]";
                    }

                    if (team.TeamTier != null)
                    {
                        showdownTier = "[" + team.TeamTier + "]";
                    }
                    if (!teamByActualTiers.ContainsKey(showdownTier))
                    {
                        teamByActualTiers.Add(showdownTier, new List<Team>());
                    }
                    team.Definition = tierDef;
                    team.RMT = false;
                    teamByActualTiers[showdownTier].Add(team);
                }
            }

            foreach (var tier in rmts)
            {
                var tierDef = tier.Key;
                foreach (var team in tier.Value)
                {
                    var showdownTier = TranslateRMTTeamsTier(tierDef);
                    if (showdownTier == null)
                    {
                        team.TeamTier = "";
                        showdownTier = "";
                    }
                    else if (team.TeamTier != null)
                    {
                        showdownTier = "[" + team.TeamTier + "]";
                    }
                    else
                    {
                        team.TeamTier = showdownTier;
                        showdownTier = "[" + showdownTier + "]";
                    }
                    if (!teamByActualTiers.ContainsKey(showdownTier))
                    {
                        teamByActualTiers.Add(showdownTier, new List<Team>());
                    }
                    team.Definition = tierDef;
                    team.RMT = true;
                    teamByActualTiers[showdownTier].Add(team);
                }
            }

            foreach (var kv in teamByActualTiers)
            {
                kv.Value.Sort((t1, t2) => { return (t2.Koeffizient.CompareTo(t1.Koeffizient) != 0) ? t2.Koeffizient.CompareTo(t1.Koeffizient) : t2.Likes.CompareTo(t1.Likes); });
            }

            var tierOutputs = new Dictionary<string, string>();
            var finalImportable = "";
            var smogonTeamCount = 1;
            foreach (var tier in teamByActualTiers)
            {
                var importable = new StringBuilder("");
                foreach (var team in tier.Value)
                {
                    var tierDef = team.Definition;
                    var lines = team.TeamString.Replace("\t", "").Replace("\r", "").Split('\n');
                    var skipTeam = false;
                    foreach (var line in lines)
                    {
                        if (line.Length > 61)
                        {
                            skipTeam = true;
                            break;
                        }
                    }
                    if (skipTeam) continue;

                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("-") && lines[i].Contains("/"))
                        {
                            lines[i] = lines[i].Substring(0, lines[i].IndexOf("/"));
                        }
                        if (lines[i].Contains("]"))
                        {
                            lines[i] = lines[i].Substring(0, lines[i].IndexOf("]") + 1);
                        }
                    }
                    var betterTeamString = string.Join("\n", lines);

                    importable.Append("=== ");
                    var showdownTier = tier.Key;
                    importable.Append(showdownTier + " ");
                    importable.Append(tierDef + "/");

                    var teamString = "#" + smogonTeamCount + " " + team.Likes + " Likes " + ((int)(team.Koeffizient)) + " Score posted by " + team.PostedBy + ((team.TeamTitle != null) ? (" " + team.TeamTitle) : "") + " " + team.URL;
                    importable.Append(teamString);
                    Console.WriteLine(teamString);

                    importable.Append(" ===\n\n");

                    importable.Append(betterTeamString);

                    importable.Append("\n\n\n\n");

                    smogonTeamCount++;
                }
                finalImportable += importable;
                tierOutputs.Add(tier.Key, importable.ToString());
            }

            tierOutputs.Add("importable", finalImportable);

            return tierOutputs;
        }

        private string TranslateSmogonTeamsTier(string tier, string url, string tag)
        {
            var workingTier = tier.ToLower();
            var toWorkWithGen = GetToWorkWithGen(workingTier);

            if (workingTier.Contains("overused"))
            {
                return toWorkWithGen + "ou";
            }
            else if (workingTier.Contains("uber"))
            {
                return toWorkWithGen + "ubers";
            }
            else if (workingTier.Contains("underused"))
            {
                return toWorkWithGen + "uu";
            }
            else if (workingTier.Contains("rarelyused"))
            {
                return toWorkWithGen + "ru";
            }
            else if (workingTier.Contains("neverused"))
            {
                return toWorkWithGen + "nu";
            }
            else if (workingTier.Contains("pu"))
            {
                return toWorkWithGen + "pu";
            }
            else if (workingTier.Contains("little cup"))
            {
                return toWorkWithGen + "lc";
            }
            else if (workingTier.Contains("doubles ou"))
            {
                return toWorkWithGen + "doublesou";
            }
            else if (workingTier.Contains("monotype"))
            {
                return toWorkWithGen + "monotype";
            }
            else if (workingTier.Contains("battle stadium"))
            {
                return toWorkWithGen + "battlestadiumsingles";
            }

            var trimUrl = url.Replace("-", " ").ToLower();
            foreach (var key in _mappingWithSpace.Keys)
            {
                if (trimUrl.Contains(key))
                {
                    foreach (var tierKey in _listOfTiersWithSpace)
                    {
                        if (trimUrl.Contains(tierKey))
                        {
                            return _mappingWithSpace[key] + tierKey.Replace(" ", "");
                        }
                    }
                    return _mappingWithSpace[key] + "ou";
                }
            }

            if (tag != null && tag.Contains("Gen "))
            {
                return tag.ToLower().Replace(" ", "");
            }

            return null;
        }

        private string GetToWorkWithGen(string workingTier)
        {
            var toWorkWithGen = Common.CurrentGen;
            if (workingTier.Contains(Common.NewestGen))
            {
                toWorkWithGen = Common.NewestGen;
            }

            return toWorkWithGen;
        }

        private string TranslateRMTTeamsTier(string tier)
        {
            var workingTier = tier.ToLower();
            var startword = "";
            if (tier.Contains(" "))
            {
                startword = workingTier.Substring(0, workingTier.IndexOf(" "));
            }

            if (startword.Contains("gen"))
            {
                return workingTier.Replace(" ", "");
            }

            if (_mapping.ContainsKey(startword))
            {
                var toAdd = "";
                var checkString = workingTier.Replace(" ", "");
                if (checkString.Contains("doubles"))
                {
                    if (!checkString.Contains("doublesou") && !checkString.Contains("doublesuu") && !checkString.Contains("doublesubers"))
                    {
                        toAdd = "ou";
                    }
                }
                return _mapping[startword] + workingTier.Substring(workingTier.IndexOf(" ") + 1).Replace(" ", "") + toAdd;
            }

            var toWorkWithGen = GetToWorkWithGen(workingTier);
            if (workingTier.Contains("monotype"))
            {
                return toWorkWithGen + "monotype";
            }
            else if (workingTier.Contains("battle spot"))
            {
                return toWorkWithGen + "battlespotsingles";
            }

            return null;
        }
    }
}
