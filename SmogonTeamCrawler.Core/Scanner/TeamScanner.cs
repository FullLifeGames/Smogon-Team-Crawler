using SmogonTeamCrawler.Core.Data;
using SmogonTeamCrawler.Core.Util;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmogonTeamCrawler.Core.Scanner
{
    public class TeamScanner : IScanner
    {
        /// <summary>Forum index sections whose sub-forums are crawled as regular forums, with the generation used as tier prefix.</summary>
        public static IList<(string Section, Func<string> GenPrefix)> RegularSections { get; set; } =
        [
            ("Smogon Metagames", () => Common.NewestGen),
            ("Ruins of Alph", () => Common.CurrentGen),
            ("Gen 8 Competitive Discussion", () => Common.CurrentGen),
        ];

        /// <summary>Forum index sections whose sub-forums are crawled as Rate My Team forums.</summary>
        public static IList<string> RmtSections { get; set; } = ["Rate My Team"];

        /// <summary>Sections of the archive category (<see cref="Common.ARCHIVE_URL"/>) whose sub-forums are crawled as Rate My Team forums.</summary>
        public static IList<string> ArchiveRmtSections { get; set; } = ["2007 - 2022 Rate My Team"];

        private readonly HttpClient? _httpClient;

        public TeamScanner() : this(null) { }

        public TeamScanner(HttpClient? httpClient)
        {
            _httpClient = httpClient;
        }

        private HttpClient HttpClient => _httpClient ?? Common.HttpClient;

        public async Task<ScanResult> Scan()
        {
            var scanResult = new ScanResult()
            {
                TierToRegularLinks = new Dictionary<string, string>(),
                TierToRmtLinks = new Dictionary<string, string>(),
            };

            var smogonMain = await HttpClient.GetStringAsync(Common.SMOGON_FORUMS_URL).ConfigureAwait(false);
            SmogonPages.EnsureTemplate(smogonMain, Common.SMOGON_FORUMS_URL, "forum_list");
            foreach (var (section, genPrefix) in RegularSections)
            {
                AddSubForums(smogonMain, section, genPrefix(), scanResult.TierToRegularLinks);
            }
            foreach (var section in RmtSections)
            {
                AddSubForums(smogonMain, section, "", scanResult.TierToRmtLinks);
            }

            var archive = await HttpClient.GetStringAsync(Common.ARCHIVE_URL).ConfigureAwait(false);
            SmogonPages.EnsureTemplate(archive, Common.ARCHIVE_URL, "category_view");
            foreach (var section in ArchiveRmtSections)
            {
                AddSubForums(archive, section, "", scanResult.TierToRmtLinks);
            }

            return scanResult;
        }

        private static void AddSubForums(string html, string section, string namePrefix, IDictionary<string, string> addition)
        {
            var subForums = ForumIndexParser.FindSubForums(html, section);
            if (subForums == null)
            {
                Console.WriteLine($"Warning: forum section '{section}' was not found; its teams are missing from this run.");
                return;
            }
            foreach (var (name, url) in subForums)
            {
                addition.TryAdd(namePrefix + name, url);
            }
        }
    }
}
