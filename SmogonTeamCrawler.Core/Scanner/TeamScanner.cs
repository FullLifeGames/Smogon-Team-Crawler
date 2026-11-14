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
        ];

        /// <summary>
        /// Forums (path below <see cref="Common.SMOGON_FORUMS_URL"/>) whose sub-forums are crawled as regular forums, read from
        /// their own page: closed forums that Smogon moved deeper into Cold Storage than the forum index shows.
        /// </summary>
        public static IList<(string Path, Func<string> GenPrefix)> RegularForumPages { get; set; } =
        [
            ("forums/gen-8-competitive-discussion.525/", () => Common.CurrentGen),
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
                AddSubForums(smogonMain, Common.SMOGON_FORUMS_URL, section, genPrefix(), scanResult.TierToRegularLinks);
            }
            foreach (var (path, genPrefix) in RegularForumPages)
            {
                await AddForumsOfPageAsync(Common.SMOGON_FORUMS_URL + path, genPrefix(), scanResult.TierToRegularLinks).ConfigureAwait(false);
            }
            foreach (var section in RmtSections)
            {
                AddSubForums(smogonMain, Common.SMOGON_FORUMS_URL, section, "", scanResult.TierToRmtLinks);
            }

            var archive = await HttpClient.GetStringAsync(Common.ARCHIVE_URL).ConfigureAwait(false);
            SmogonPages.EnsureTemplate(archive, Common.ARCHIVE_URL, "category_view");
            foreach (var section in ArchiveRmtSections)
            {
                AddSubForums(archive, Common.ARCHIVE_URL, section, "", scanResult.TierToRmtLinks);
            }

            return scanResult;
        }

        private async Task AddForumsOfPageAsync(string url, string namePrefix, IDictionary<string, string> addition)
        {
            using var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (SmogonPages.IsBotCheck(html))
            {
                throw new SmogonAccessBlockedException(url);
            }
            if (!response.IsSuccessStatusCode || !html.Contains("data-template=\"forum_view\""))
            {
                Console.WriteLine($"Warning: the forum {url} was not found ({(int)response.StatusCode}); its teams are missing from this run.");
                return;
            }
            var forums = ForumIndexParser.FindForums(html);
            if (forums.Count == 0)
            {
                // Changed markup: a run without these forums would write team files without their teams.
                throw new UnexpectedPageException(url, "the forum lists no sub-forums");
            }
            foreach (var (name, forumUrl) in forums)
            {
                addition.TryAdd(namePrefix + name, forumUrl);
            }
        }

        /// <summary>
        /// A missing section is skipped with a warning (Smogon moves closed forums); a section without sub-forums means
        /// changed markup and stops the crawl, before the team files are written without its teams.
        /// </summary>
        private static void AddSubForums(string html, string pageUrl, string section, string namePrefix, IDictionary<string, string> addition)
        {
            var subForums = ForumIndexParser.FindSubForums(html, section);
            if (subForums == null)
            {
                Console.WriteLine($"Warning: forum section '{section}' was not found; its teams are missing from this run.");
                return;
            }
            if (subForums.Count == 0)
            {
                throw new UnexpectedPageException(pageUrl, $"the section '{section}' lists no sub-forums");
            }
            foreach (var (name, url) in subForums)
            {
                addition.TryAdd(namePrefix + name, url);
            }
        }
    }
}
