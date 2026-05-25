using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Index;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IIndexService
    {
        Task<UpstreamIndex> BuildUpstreamIndexAsync(string targetLayer);

        Task<UpstreamIndex> BuildUpstreamIndexAsync(string targetLayer, string? sourceBookId);

        IndexItem BuildIndexItem<T>(T item, bool useDeepSummary = false) where T : IIndexable;

        Task<List<T>> LoadByIdsAsync<T>(List<string> ids) where T : class;
    }
}
