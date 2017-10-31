using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smogon_Team_Crawler
{
    public class Team
    {
        public string TeamString;
        public int Likes;
        public DateTime PostDate;
        public string URL;

        public double Koeffizient;
        private static double koeffScale = 5;

        public Team(string teamString, int likes, DateTime postDate, string url)
        {
            this.TeamString = teamString;
            this.Likes = likes;
            this.PostDate = postDate;
            this.URL = url;

            calculateKoeffizient();
        }

        private void calculateKoeffizient()
        {
            this.Koeffizient = this.Likes * 
                                (1.0 / 
                                        (
                                            (DateTime.Now.Subtract(this.PostDate).TotalMinutes / 60.0 / 24.0 / 365.0) * koeffScale + 1
                                        )
                                );
        }

        public bool Equals(Team team)
        {
            return this.TeamString == team.TeamString;
        }
    }
}
