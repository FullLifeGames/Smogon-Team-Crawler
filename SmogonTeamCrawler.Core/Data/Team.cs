using SmogonTeamCrawler.Core.Util;
using System;

namespace SmogonTeamCrawler.Core.Data
{
    public class Team
    {
        private string _teamString = null!;
        public string TeamString
        {
            get { return _teamString; }
            set
            {
                _teamString = value;
                RegexTeam = Common.TeamRegex.Replace(_teamString, "");
            }
        }
        private string RegexTeam { get; set; } = null!;
        public int Likes { get; set; }
        public DateTime PostDate { get; set; }
        public string URL { get; set; }
        public string PostedBy { get; set; }
        public string? TeamTitle { get; set; }
        public string? TeamTier { get; set; }
        public string? TeamLineUp { get; set; }

        public string? TeamTag { get; set; }

        public bool RMT { get; set; }
        public string? Definition { get; set; }

        public double Koeffizient { get {
            return Likes *
                (1.0 /
                    (
                        Math.Exp(Common.KoeffScale * (DateTime.Now.Subtract(PostDate).TotalMinutes / 60.0 / 24.0 / 365.0))
                    )
                );
        } }

        public Team(string teamString, int likes, DateTime postDate, string url, string postedBy, string? prefix)
        {
            TeamString = teamString.Replace("\t", "");
            Likes = likes;
            PostDate = postDate;
            URL = url;
            PostedBy = postedBy;
            TeamTag = prefix;
        }

        public bool EqualsOrEmpty(Team team)
        {
            if (team.RegexTeam?.Length == 0)
            {
                return true;
            }
            return RegexTeam == team.RegexTeam;
        }
    }
}
