using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Transformer
{
    public interface ITransformer
    {
        public Dictionary<string, string> Transform(Dictionary<string, List<Team>> smogonTeams, Dictionary<string, List<Team>> rmts);
    }
}
