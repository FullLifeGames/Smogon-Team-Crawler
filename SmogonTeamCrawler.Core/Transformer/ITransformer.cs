using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Transformer
{
    public interface ITransformer
    {
        public Dictionary<string, string> Transform(IDictionary<string, ICollection<Team>> smogonTeams, IDictionary<string, ICollection<Team>> rmts);
    }
}
