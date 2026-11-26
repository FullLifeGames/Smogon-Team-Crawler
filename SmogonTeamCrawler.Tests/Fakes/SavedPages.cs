#nullable enable
using NUnit.Framework;
using System.IO;

namespace SmogonTeamCrawler.Tests.Fakes
{
    /// <summary>Pages as Smogon served them to the crawler's browser, saved unchanged in Fixtures/.</summary>
    internal static class SavedPages
    {
        /// <summary>
        /// The search for <see cref="Core.Collector.CollectorOptions.PasteQuery"/> in OU (726) and National Dex UU (859) since
        /// 2026-11-24, loaded on 2026-11-26: two posts.
        /// </summary>
        public static string SearchResults => Read("search-results-2026-11-26.html");

        /// <summary>Page 1 of the Tournaments forum (34), loaded on 2026-11-26.</summary>
        public static string ForumListing => Read("forum-listing-2026-11-26.html");

        private static string Read(string name) => File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));
    }
}
