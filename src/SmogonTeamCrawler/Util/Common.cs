using System.Net.Http;

namespace SmogonTeamCrawler.Util
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

        public static bool mainForum = true;
        public static bool rmtForum = true;

        public static string[] hardCodedBlacklistedPastes = new string[] { "spEtvevT", "Cyg8Rp53", "c6EFsDjr" };
    }
}
