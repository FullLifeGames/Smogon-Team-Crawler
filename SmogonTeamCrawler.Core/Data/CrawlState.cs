using System;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Data
{
    /// <summary>One thread as listed on a forum listing page.</summary>
    public class ThreadListItem
    {
        public long ThreadId { get; set; }
        /// <summary>Absolute thread URL with trailing slash, e.g. https://www.smogon.com/forums/threads/some-title.123/</summary>
        public string Url { get; set; } = "";
        public string Prefix { get; set; } = "";
        public bool Sticky { get; set; }
        /// <summary>Unix timestamp (seconds) of the latest post in the thread.</summary>
        public long LatestPostTimestamp { get; set; }
        /// <summary>The latest post date as Smogon displays it, e.g. "Nov 8, 2026 at 11:12 AM".</summary>
        public string LatestPostDisplayDate { get; set; } = "";
        public int LastPage { get; set; } = 1;
    }

    /// <summary>What the crawler remembers about a forum between runs.</summary>
    public class ForumCrawlState
    {
        /// <summary>Newest latest-post timestamp seen in the forum listing during the last run.</summary>
        public long Watermark { get; set; }
        /// <summary>True once the whole listing was walked; afterwards only the changed part is read.</summary>
        public bool Complete { get; set; }
        public DateTime LastCrawl { get; set; }
        public Dictionary<long, ForumThreadEntry> Threads { get; set; } = [];
        /// <summary>Threads that could not be read; they are tried again in the next run even if the listing walk stops before them.</summary>
        public Dictionary<long, ThreadListItem> RetryThreads { get; set; } = [];
    }

    public class ForumThreadEntry
    {
        public string Url { get; set; } = "";
        public string Prefix { get; set; } = "";
    }

    /// <summary>What the crawler remembers about a thread between runs.</summary>
    public class ThreadCrawlState
    {
        public string Url { get; set; } = "";
        /// <summary>The forum whose listing showed this thread last; used to drop threads that were moved.</summary>
        public string ForumUrl { get; set; } = "";
        /// <summary>Unix timestamp (seconds) of the latest post when the thread was last read; 0 means unknown.</summary>
        public long LatestPostTimestamp { get; set; }
        public int NumberOfPages { get; set; }
        public Dictionary<int, PageCrawlState> Pages { get; set; } = [];
    }

    public class PageCrawlState
    {
        public List<Team> Teams { get; set; } = [];
        /// <summary>Date of the newest post on the page, as displayed by Smogon.</summary>
        public DateTime NewestPostDate { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}
