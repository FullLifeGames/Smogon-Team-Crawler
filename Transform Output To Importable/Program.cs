using Smogon_Team_Crawler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Transform_Output_To_Importable
{
    class Program
    {
        static void Main(string[] args)
        {
            StreamReader sr = new StreamReader("../../../Smogon Team Crawler/bin/Release/outputJson.txt");
            Dictionary<string, List<Team>> smogonTeams = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Team>>>(sr.ReadToEnd());
            sr.Close();

            sr = new StreamReader("../../../Smogon Team Crawler/bin/Release/outputRMTJson.txt");
            Dictionary<string, List<Team>> rmts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<Team>>>(sr.ReadToEnd());
            sr.Close();

            string importable = "";

            int smogonTeamCount = 1;

            foreach(KeyValuePair<string, List<Team>> tier in smogonTeams)
            {
                string tierDef = tier.Key;
                foreach(Team team in tier.Value)
                {
                    string[] lines = team.TeamString.Replace("\t", "").Replace("\r", "").Split('\n');
                    bool skipTeam = false;
                    foreach(string line in lines)
                    {
                        if(line.Length > 61)
                        {
                            skipTeam = true;
                            break;
                        }
                    }
                    if (skipTeam) continue;

                    for(int i= 0; i < lines.Length; i++)
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

                    importable += "=== ";
                    string showdownTier = TranslateSmogonTeamsTier(tierDef, team.URL);
                    if(showdownTier == null)
                    {
                        showdownTier = "";
                    }
                    else
                    {
                        showdownTier = "[" + showdownTier + "] ";
                    }

                    if (team.TeamTier != null)
                    {
                        importable += "[" + team.TeamTier + "]";
                    }
                    else
                    {
                        importable += showdownTier;
                    }
                    importable += tierDef + "/";

                    string teamString = "Smogon Nr. " + smogonTeamCount + " " + team.Likes + " Likes " + ((int)(team.Koeffizient)) + " Score posted by " + team.PostedBy + ((team.TeamTitle != null) ? (" " + team.TeamTitle) : "");
                    importable += teamString;
                    Console.WriteLine(teamString);

                    importable += " ===\n\n";

                    importable += betterTeamString;

                    importable += "\n\n\n\n";

                    smogonTeamCount++;
                }
            }

            int rmtTeamCount = 1;

            foreach (KeyValuePair<string, List<Team>> tier in rmts)
            {
                string tierDef = tier.Key;
                foreach (Team team in tier.Value)
                {
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

                    importable += "=== ";
                    string showdownTier = TranslateRMTTeamsTier(tierDef);
                    if (showdownTier == null)
                    {
                        showdownTier = "";
                    }
                    else
                    {
                        showdownTier = "[" + showdownTier + "] ";
                    }

                    if (team.TeamTier != null)
                    {
                        importable += "[" + team.TeamTier + "]";
                    }
                    else
                    {
                        importable += showdownTier;
                    }
                    importable += tierDef + "/";

                    string teamString = "RMT Nr. " + rmtTeamCount + " " + team.Likes + " Likes " + ((int)(team.Koeffizient)) + " Score posted by " + team.PostedBy + ((team.TeamTitle != null) ? (" " + team.TeamTitle) : "");
                    importable += teamString;
                    Console.WriteLine(teamString);

                    importable += " ===\n\n";

                    importable += betterTeamString;

                    importable += "\n\n\n\n";

                    rmtTeamCount++;
                }
            }

            importable = importable.Replace("\n", "\r\n");

            StreamWriter sw = new StreamWriter("importable.txt");
            sw.Write(importable);
            sw.Close();

        }

        private static string TranslateSmogonTeamsTier(string tier, string url)
        {
            string workingTier = tier.ToLower();
            if (workingTier.Contains("overused"))
            {
                return "gen7ou";
            }
            else if (workingTier.Contains("uber"))
            {
                return "gen7ubers";
            }
            else if (workingTier.Contains("underused"))
            {
                return "gen7uu";
            }
            else if(workingTier.Contains("rarelyused"))
            {
                return "gen7ru";
            }
            else if (workingTier.Contains("neverused"))
            {
                return "gen7nu";
            }
            else if (workingTier.Contains("pu"))
            {
                return "gen7pu";
            }
            else if(workingTier.Contains("little cup"))
            {
                return "gen7lc";
            }
            else if (workingTier.Contains("doubles ou"))
            {
                return "gen7doublesou";
            }
            else if (workingTier.Contains("monotype"))
            {
                return "gen7monotype";
            }

            string trimUrl = url.Replace("-", " ").ToLower();
            foreach(string key in mappingWithSpace.Keys)
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

            return null;            
        }

        private static Dictionary<string, string> mapping = new Dictionary<string, string>
            {
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

        private static Dictionary<string, string> mappingWithSpace = new Dictionary<string, string>
            {
                { "sm ", "gen7" },
                { "oras ", "gen6" },
                { "xy ", "gen6" },
                { "bw ", "gen5" },
                { "bw2 ", "gen5" },
                { "dpp ", "gen4" },
                { "adv ", "gen3" },
                { "adv. ", "gen3" },
                { "gsc ", "gen2" },
                { "rby ", "gen1" },
                { "stadium ", "gen1" },
            };

        private static List<string> listOfTiers = new List<string>
        {
            "ou",
            "uu",
            "ru",
            "nu",
            "pu",
            "ubers",
            "doubles"
        };

        private static List<string> listOfTiersWithSpace = new List<string>
        {
            "ou ",
            "uu ",
            "ru ",
            "nu ",
            "pu ",
            "ubers ",
            "doubles "
        };

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

            if (workingTier.Contains("monotype"))
            {
                return "gen7monotype";
            }
            else if(workingTier.Contains("battle spot"))
            {
                return "gen7battlespotsingles";
            }

            return null;
        }
    }
}
