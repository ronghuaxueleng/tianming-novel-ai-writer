using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IGlobalSummaryService
    {
        Task<GlobalSummary> GetGlobalSummaryAsync();

        Task<GlobalSummary> ComputeRealTimeAsync();

        Task<bool> CacheExistsAsync();

        void InvalidateCache();
    }
}
