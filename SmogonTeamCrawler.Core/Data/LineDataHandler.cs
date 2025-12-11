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
        public string PostedBy { get; set; } = "";
        public string LastLine { get; set; } = "";
        public string PostLink { get; set; } = "";
        public bool LikeStarted { get; set; }
        public bool TimerHeader { get; set; }
    }
}
