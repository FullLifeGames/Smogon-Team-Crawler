using System;
using System.Text;

namespace SmogonTeamCrawler.Core.Data
{
    public class LineDataHandler
    {
        public bool BlockStarted { get; set; }
        public string BlockText { get; set; } = "";
        public StringBuilder? BlockTextBuilder { get; set; }
        public bool PostStarted { get; set; }
        public int PostLikes { get; set; }
        public DateTime PostDate { get; set; } = DateTime.Now;
        /// <summary>Whether <see cref="PostDate"/> was read from the current post.</summary>
        public bool PostDateRead { get; set; }
        /// <summary>The link of the first post with a team whose author or date could not be read.</summary>
        public string? UnreadablePost { get; set; }
        public DateTime NewestPostDate { get; set; } = DateTime.MinValue;
        public string PostedBy { get; set; } = "";
        public string LastLine { get; set; } = "";
        public string PostLink { get; set; } = "";
        public bool LikeStarted { get; set; }
        public bool TimerHeader { get; set; }
        /// <summary>The lines of the current post, for teams typed into its text.</summary>
        public StringBuilder PostHtml { get; } = new();
        public bool PostMayHaveSets { get; set; }
    }
}
