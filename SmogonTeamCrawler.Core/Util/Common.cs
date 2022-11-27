using System.Net.Http;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Util
{
    public static class Common
    {
        private static HttpClient _httpClient;
        public static HttpClient HttpClient
        {
            get
            {
                if (_httpClient == null)
                {
                    _httpClient = new HttpClient();
                }
                return _httpClient;
            }
            set => _httpClient = value;
        }

        public static string CurrentGen { get; set; } = "gen8";
        public static string NewestGen { get; set; } = "gen9";

        public static string[] HardCodedBlacklistedPastes = new string[] { "spEtvevT", "Cyg8Rp53", "c6EFsDjr" };

        public static double KoeffScale = 3;

        public static Regex TeamRegex = new("[^0-9a-zA-Z]+");

        public static string SMOGON_FORUMS_URL { get; set; } = "https://www.smogon.com/forums/";
        public static string ARCHIVE_URL { get; set; } = "https://www.smogon.com/forums/categories/site-projects-archive.423/";
    }
}
