using System;
using System.Collections.Generic;
using System.Net.Http;

namespace SmogonTeamCrawler.Core.Collector
{
    public class CollectorOptions
    {
        /// <summary>Posts that link to a paste or a Showdown team.</summary>
        public const string PasteQuery = "pokepast.es | pastebin.com | hastebin.com | psim.us | teams.pokemonshowdown.com | play.pokemonshowdown.com";

        /// <summary>
        /// Posts with a team written into them: the word "Nature" and natures of two groups (physical, special). A team
        /// almost always has both, a single set only one. Smogon's search takes about 22 words, so the five neutral natures
        /// are left out.
        /// </summary>
        public const string TypedTeamQuery = "Nature (Adamant | Jolly | Impish | Careful | Sassy | Brave | Hasty | Lax | Gentle | Rash) (Timid | Modest | Bold | Calm | Relaxed | Naive | Quiet | Lonely | Naughty | Mild)";

        /// <summary>Ignore the stored state: walk every listing page and read every thread page again.</summary>
        public bool FullRefresh { get; set; }

        /// <summary>
        /// Keeps likes on recent teams current. Search runs read a page with teams younger than this again once its newest
        /// team is twice as old as at the last read; listing walks re-read, of a thread with new posts, the pages whose newest
        /// post is younger than this. <see cref="TimeSpan.Zero"/> turns this off.
        /// </summary>
        public TimeSpan RecentPageRefreshWindow { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Find team posts through Smogon's search instead of walking the forum listings, and read only the pages with hits:
        /// on the first run all posts of a forum, later the posts since the last run. A forum is walked when the search is
        /// not available for it, and always with <see cref="FullRefresh"/> or a name from <see cref="ListingForums"/>.
        /// </summary>
        public bool UseSearch { get; set; } = true;

        /// <summary>
        /// Searches for team posts, in Smogon's syntax: all words are required, "|" is or, quotes make a phrase, parentheses
        /// group. A post is a hit if it matches one of them.
        /// </summary>
        public IList<string> SearchQueries { get; set; } = [PasteQuery, TypedTeamQuery];

        /// <summary>
        /// Forums whose name (the tier key) contains one of these, ignoring case, are walked instead of searched. Teams of
        /// generations 1 and 2 have no natures, so <see cref="TypedTeamQuery"/> does not find them when they are written into a post.
        /// </summary>
        public IList<string> ListingForums { get; set; } = ["RBY", "GSC", "Past Gen", "Old Gen", "Oldgen"];

        /// <summary>HttpClient for all requests; defaults to <see cref="Util.Common.HttpClient"/>.</summary>
        public HttpClient? HttpClient { get; set; }
    }
}
