using Smogon_Team_Crawler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Transform_Output_To_Importable
{
    public class Program
    {
        public static string CurrentNewGen = "gen8";
        public static string CurrentWorkingGen = "gen7";

        private static Dictionary<string, string> mapping = new Dictionary<string, string>
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
        private static Dictionary<string, string> mappingWithSpace = mapping.Select((val) => new KeyValuePair<string, string>(val.Key + " ", val.Value))
                                                                               .ToDictionary(x => x.Key, x => x.Value);

        private static List<string> listOfTiers = new List<string>
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
        private static List<string> listOfTiersWithSpace = listOfTiers.Select((val) => val + " ").ToList();

        public static void Main(string[] args)
        {
            StreamReader sr = new StreamReader("outputJson.txt");
            Dictionary<string, List<Team>> smogonTeams = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Team>>>(sr.ReadToEnd());
            sr.Close();

            sr = new StreamReader("outputRMTJson.txt");
            Dictionary<string, List<Team>> rmts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Team>>>(sr.ReadToEnd());
            sr.Close();
                        
            Dictionary<string, List<Team>> teamByActualTiers = new Dictionary<string, List<Team>>();

            foreach (KeyValuePair<string, List<Team>> tier in smogonTeams)
            {
                string tierDef = tier.Key;
                foreach (Team team in tier.Value)
                {
                    string showdownTier = TranslateSmogonTeamsTier(tierDef, team.URL, team.TeamTag);
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

            foreach (KeyValuePair<string, List<Team>> tier in rmts)
            {
                string tierDef = tier.Key;
                foreach (Team team in tier.Value)
                {
                    string showdownTier = TranslateRMTTeamsTier(tierDef);
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

            foreach (KeyValuePair<string, List<Team>> kv in teamByActualTiers)
            {
                kv.Value.Sort((t1, t2) => { return (t2.Koeffizient.CompareTo(t1.Koeffizient) != 0) ? t2.Koeffizient.CompareTo(t1.Koeffizient) : t2.Likes.CompareTo(t1.Likes); });
            }


            Dictionary<string, string> tierOutputs = new Dictionary<string, string>();
            string finalImportable = "";
            int smogonTeamCount = 1;
            foreach (KeyValuePair<string, List<Team>> tier in teamByActualTiers)
            {
                StringBuilder importable = new StringBuilder("");
                foreach (Team team in tier.Value)
                {
                    string tierDef = team.Definition;
                    string[] lines = team.TeamString.Replace("\t", "").Replace("\r", "").Split('\n');
                    bool skipTeam = false;
                    foreach (string line in lines)
                    {
                        if (line.Length > 61)
                        {
                            skipTeam = true;
                            break;
                        }
                    }
                    if (skipTeam) continue;

                    for (int i = 0; i < lines.Length; i++)
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
                    string betterTeamString = String.Join("\n", lines);

                    importable.Append("=== ");
                    string showdownTier = tier.Key;
                    importable.Append(showdownTier + " ");
                    importable.Append(tierDef + "/");

                    string teamString = "#" + smogonTeamCount + " " + team.Likes + " Likes " + ((int)(team.Koeffizient)) + " Score posted by " + team.PostedBy + ((team.TeamTitle != null) ? (" " + team.TeamTitle) : "") + " " + team.URL;
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
            StreamWriter sw;
            foreach (KeyValuePair<string, string> outputs in tierOutputs)
            {
                string tempImportable = outputs.Value.Replace("\n", "\r\n");
                string tempTier = (outputs.Key != "") ? outputs.Key : "undefined";

                sw = new StreamWriter(tempTier.Replace("[", "").Replace("]", "") + ".txt");
                sw.Write(tempImportable);
                sw.Close();
            }

            sw = new StreamWriter("finalJson.txt");
            sw.Write(Newtonsoft.Json.JsonConvert.SerializeObject(tierOutputs));
            sw.Close();
        }

        private static string TranslateSmogonTeamsTier(string tier, string url, string tag)
        {
            string workingTier = tier.ToLower();
            string toWorkWithGen = GetToWorkWithGen(workingTier);

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

            string trimUrl = url.Replace("-", " ").ToLower();
            foreach (string key in mappingWithSpace.Keys)
            {
                if (trimUrl.Contains(key))
                {
                    foreach (string tierKey in listOfTiersWithSpace)
                    {
                        if (trimUrl.Contains(tierKey))
                        {
                            return mappingWithSpace[key] + tierKey.Replace(" ", "");
                        }
                    }
                    return mappingWithSpace[key] + "ou";
                }
            }

            if (tag != null && tag.Contains("Gen "))
            {
                return tag.ToLower().Replace(" ", "");
            }

            return null;
        }

        private static string GetToWorkWithGen(string workingTier)
        {
            string toWorkWithGen = CurrentWorkingGen;
            if (workingTier.Contains(CurrentNewGen))
            {
                toWorkWithGen = CurrentNewGen;
            }

            return toWorkWithGen;
        }

        private static string TranslateRMTTeamsTier(string tier)
        {
            string workingTier = tier.ToLower();
            string startword = "";
            if (tier.Contains(" "))
            {
                startword = workingTier.Substring(0, workingTier.IndexOf(" "));
            }

            if (startword.Contains("gen"))
            {
                return workingTier.Replace(" ", "");
            }

            if (mapping.ContainsKey(startword))
            {
                string toAdd = "";
                string checkString = workingTier.Replace(" ", "");
                if (checkString.Contains("doubles"))
                {
                    if (!checkString.Contains("doublesou") && !checkString.Contains("doublesuu") && !checkString.Contains("doublesubers"))
                    {
                        toAdd = "ou";
                    }
                }
                return mapping[startword] + workingTier.Substring(workingTier.IndexOf(" ") + 1).Replace(" ", "") + toAdd;
            }

            string toWorkWithGen = GetToWorkWithGen(workingTier);
            if (workingTier.Contains("monotype"))
            {
                return toWorkWithGen + "monotype";
            }
            else if(workingTier.Contains("battle spot"))
            {
                return toWorkWithGen + "battlespotsingles";
            }

            return null;
        }
    }
}
