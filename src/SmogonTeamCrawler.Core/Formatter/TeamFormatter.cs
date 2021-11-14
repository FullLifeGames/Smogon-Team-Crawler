using SmogonTeamCrawler.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmogonTeamCrawler.Core.Formatter
{
    public class TeamFormatter : IFormatter
    {
        public string FormatOutput(IDictionary<string, ICollection<Team>> teamsForTiers)
        {
            var output = "";
            foreach (string key in teamsForTiers.Keys.ToList())
            {
                if (teamsForTiers[key].Count == 0)
                {
                    continue;
                }

                output += "Smogon (" + key + "):\n\n";
                teamsForTiers[key] = RemoveDuplicates(teamsForTiers[key]);
                ((List<Team>) teamsForTiers[key]).Sort((t1, t2) => { return t2.Koeffizient.CompareTo(t1.Koeffizient); });

                foreach (Team team in teamsForTiers[key])
                {
                    var lineup = GetTeamLineupString(team.TeamString);
                    team.TeamLineUp = lineup;
                    var outputString = "";
                    var lines = new List<string>();
                    foreach (var monData in team.TeamString.Split(new string[] { "\n\n" }, StringSplitOptions.None))
                    {
                        if (!monData.Contains('\n'))
                        {
                            lines.Add(monData);
                            continue;
                        }
                        string mon = monData.Substring(0, monData.IndexOf("\n"));
                        mon = mon.Replace(":", "");
                        lines.Add(mon + monData.Substring(monData.IndexOf("\n")));
                    }
                    outputString = string.Join("\n\n", lines);
                    output += lineup + ":\n" + team.URL + "\n" + team.PostedBy + "\n" + team.PostDate.ToString() + "\n" + team.Likes + " Likes\n" + team.Koeffizient + " Calculated Value\n\n" + outputString + "\n\n\n";
                }

                output += "\n";
            }
            return output;
        }


        private List<Team> RemoveDuplicates(ICollection<Team> value)
        {
            var uniqueTeams = new List<Team>();
            foreach (var team in value)
            {
                bool unique = true;
                foreach (var uniqueTeam in uniqueTeams)
                {
                    if (uniqueTeam.EqualsOrEmpty(team))
                    {
                        unique = false;
                        break;
                    }
                }
                if (unique)
                {
                    uniqueTeams.Add(team);
                }
            }
            return uniqueTeams;
        }

        private string GetTeamLineupString(string teamString)
        {
            var mons = new List<string>();
            foreach (var monData in teamString.Split(new string[] { "\n\n" }, StringSplitOptions.None))
            {
                if (!monData.Contains('\n'))
                {
                    continue;
                }
                var mon = monData[..monData.IndexOf("\n")];
                mon = mon.Replace("(M)", "");
                mon = mon.Replace("(F)", "");
                if (mon.Contains('@'))
                {
                    mon = mon[..mon.IndexOf("@")].Trim();
                }
                if (mon.Contains('(') && mon.Contains(')'))
                {
                    mon = mon[(mon.IndexOf("(") + 1)..];
                    mon = mon[..mon.IndexOf(")")];
                }
                mons.Add(mon.Trim());
            }

            return string.Join(", ", mons);
        }
    }
}
