﻿using System.Net.Http;
using System.Text.RegularExpressions;

namespace SmogonTeamCrawler.Core.Util
{
    public static class Common
    {
        private static HttpClient? _httpClient;
        public static HttpClient HttpClient
        {
            get
            {
                _httpClient ??= new HttpClient(new HttpRetryMessageHandler(new HttpClientHandler()));
                return _httpClient;
            }
            set => _httpClient = value;
        }

        public static string CurrentGen { get; set; } = "gen8";
        public static string NewestGen { get; set; } = "gen9";

        public static string[] HardCodedBlacklistedPastes { get; set; } = new string[] { "spEtvevT", "Cyg8Rp53", "c6EFsDjr", "a155d09dc696b334" };

        public static double KoeffScale { get; set; } = 3;

        public static Regex TeamRegex { get; set; } = new("[^0-9a-zA-Z]+");

        public static string SMOGON_FORUMS_URL { get; set; } = "https://www.smogon.com/forums/";
        public static string ARCHIVE_URL { get; set; } = "https://www.smogon.com/forums/categories/site-projects-archive.423/";
    }
}
