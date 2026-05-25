using System;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IWorkScopeService
    {
        string? CurrentSourceBookId { get; }

        DateTime? LastUpdated { get; }

        event EventHandler<string?>? ScopeChanged;

        Task InitializeAsync();

        Task<string?> GetCurrentScopeAsync();

        Task SetCurrentScopeAsync(string? sourceBookId);

        Task ClearScopeAsync();
    }
}
