using SmogonTeamCrawler.Core.Data;
using System.Collections.Generic;

namespace SmogonTeamCrawler.Core.Transformer
{
    public interface ITransformer
    {
        public IDictionary<string, string> Transform(IDictionary<string, ICollection<Team>> smogonTeams, IDictionary<string, ICollection<Team>> rmts);
        public IDictionary<string, ICollection<Team>> CreateTeamsByTiers(IDictionary<string, ICollection<Team>> smogonTeams, IDictionary<string, ICollection<Team>> rmts);
    }
}
