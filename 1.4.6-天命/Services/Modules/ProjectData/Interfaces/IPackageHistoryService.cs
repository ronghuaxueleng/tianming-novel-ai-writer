using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IPackageHistoryService
    {
        int RetainCount { get; set; }

        Task<bool> SaveCurrentToHistoryAsync();

        List<PackageHistoryEntry> GetAllHistory();

        Task<bool> RestoreVersionAsync(int version);

        void CleanupOldHistory();

        PackageVersionDiff GetVersionDiff(int historyVersion);

        Task<bool> ClearAllAsync();
    }
}
