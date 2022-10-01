using SmogonTeamCrawler.Core.Util;
using System;

namespace SmogonTeamCrawler.Core.Data
{
    public class Team
    {
        public string TeamString { get; set; }
        public int Likes { get; set; }
        public DateTime PostDate { get; set; }
        public string URL { get; set; }
        public string PostedBy { get; set; }
        public string TeamTitle { get; set; } = null;
        public string TeamTier { get; set; } = null;
        public string TeamLineUp { get; set; } = null;
        private string RegexTeam { get; set; }

        public string TeamTag { get; set; } = null;

        public bool RMT { get; set; } = false;
        public string Definition { get; set; } = null;

        public double Koeffizient { get {
            return Likes *
                (1.0 /
                    (
                        Math.Exp(Common.KoeffScale * (DateTime.Now.Subtract(PostDate).TotalMinutes / 60.0 / 24.0 / 365.0))
                    )
                );
        } }

        public Team(string teamString, int likes, DateTime postDate, string url, string postedBy, string prefix)
        {
            TeamString = teamString.Replace("\t", "");
            Likes = likes;
            PostDate = postDate;
            URL = url;
            PostedBy = postedBy;
            TeamTag = prefix;
            RegexTeam = Common.TeamRegex.Replace(teamString, "");
        }

        public bool EqualsOrEmpty(Team team)
        {
            if (team.RegexTeam == "")
            {
                return true;
            }
            return RegexTeam == team.RegexTeam;
        }
    }
}
