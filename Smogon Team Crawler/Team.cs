﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Smogon_Team_Crawler
{
    public class Team
    {
        private static Regex regex = new Regex("[^0-9a-zA-Z]+");

        public string TeamString;
        public int Likes;
        public DateTime PostDate;
        public string URL;
        public string PostedBy;
        public string TeamTitle = null;
        public string TeamTier = null;
        public string TeamLineUp = null;
        private string RegexTeam;

        public string TeamTag = null;

        public bool RMT = false;
        public string Definition = null;

        public double Koeffizient;
        private static double koeffScale = 3;

        public Team(string teamString, int likes, DateTime postDate, string url, string postedBy, string prefix)
        {
            this.TeamString = teamString.Replace("\t", "");
            this.Likes = likes;
            this.PostDate = postDate;
            this.URL = url;
            this.PostedBy = postedBy;
            this.TeamTag = prefix;
            this.RegexTeam = regex.Replace(teamString, "");

            CalculateKoeffizient();
        }

        private void CalculateKoeffizient()
        {
            this.Koeffizient = this.Likes * 
                                (1.0 / 
                                        (
                                            Math.Exp(koeffScale * (DateTime.Now.Subtract(this.PostDate).TotalMinutes / 60.0 / 24.0 / 365.0))
                                        )
                                );
        }

        public bool EqualsOrEmpty(Team team)
        {
            if (team.RegexTeam == "")
            {
                return true;
            }
            return this.RegexTeam == team.RegexTeam;
        }
    }
}
