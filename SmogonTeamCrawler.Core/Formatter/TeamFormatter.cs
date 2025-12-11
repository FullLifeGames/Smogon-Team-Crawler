using SmogonTeamCrawler.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmogonTeamCrawler.Core.Formatter
{
    public class TeamFormatter : IFormatter
    {
        public string FormatOutput(IDictionary<string, ICollection<Team>> teamsForTiers)
        {
            var output = new StringBuilder();
            foreach (var key in teamsForTiers.Keys.ToList())
            {
                if (teamsForTiers[key].Count == 0)
                {
                    continue;
                }

                output.Append("Smogon (").Append(key).Append("):\n\n");
                teamsForTiers[key] = RemoveDuplicates(teamsForTiers[key]);
                var teamList = (List<Team>)teamsForTiers[key];
                teamList.Sort((t1, t2) => t2.Koeffizient.CompareTo(t1.Koeffizient));

                foreach (var team in teamList)
                {
                    var lineup = GetTeamLineupString(team.TeamString);
                    team.TeamLineUp = lineup;
                    var lines = new List<string>();
                    foreach (var monData in team.TeamString.Split(["\n\n"], StringSplitOptions.None))
                    {
                        if (!monData.Contains('\n'))
                        {
                            lines.Add(monData);
                            continue;
                        }
                        var monIndex = monData.IndexOf("\n");
                        var mon = monData[..monIndex];
                        mon = mon.Replace(":", "");
                        lines.Add(string.Concat(mon, monData.AsSpan(monIndex)));
                    }
                    var outputString = string.Join("\n\n", lines);
                    output.Append(lineup).Append(":\n")
                          .Append(team.URL).Append("\n")
                          .Append(team.PostedBy).Append("\n")
                          .Append(team.PostDate.ToString()).Append("\n")
                          .Append(team.Likes).Append(" Likes\n")
                          .Append(team.Koeffizient).Append(" Calculated Value\n\n")
                          .Append(outputString).Append("\n\n\n");
                }

                output.Append("\n");
            }
            return output.ToString();
        }

        private static ICollection<Team> RemoveDuplicates(ICollection<Team> value)
        {
            var uniqueTeams = new List<Team>();
            var seenHashes = new HashSet<string>();
            
            foreach (var team in value)
            {
                var teamHash = team.RegexTeam ?? "";
                
                if (string.IsNullOrEmpty(teamHash) || !seenHashes.Contains(teamHash))
                {
                    if (!string.IsNullOrEmpty(teamHash))
                    {
                        seenHashes.Add(teamHash);
                    }
                    uniqueTeams.Add(team);
                }
            }
            return uniqueTeams;
        }

        private static string GetTeamLineupString(string teamString)
        {
            var mons = new List<string>();
            foreach (var monData in teamString.Split(["\n\n"], StringSplitOptions.None))
            {
                if (!monData.Contains('\n'))
                {
                    continue;
                }
                var monIndex = monData.IndexOf("\n");
                var mon = monData[..monIndex];
                mon = mon.Replace("(M)", "").Replace("(F)", "");
                if (mon.Contains('@'))
                {
                    var atIndex = mon.IndexOf('@');
                    mon = mon[..atIndex].Trim();
                }
                if (mon.Contains('(') && mon.Contains(')'))
                {
                    var openIndex = mon.IndexOf('(') + 1;
                    var closeIndex = mon.IndexOf(')');
                    if (closeIndex > openIndex)
                    {
                        mon = mon[openIndex..closeIndex];
                    }
                }
                mons.Add(mon.Trim());
            }

            return string.Join(", ", mons);
        }
    }
}
