using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Contexts;
using TM.Services.Modules.ProjectData.Models.Validation;

namespace TM.Services.Modules.ProjectData.Interfaces
{
    public interface IValidationReportService
    {
        Task<ValidationReport> ValidateChapterAsync(string chapterId);

        Task<List<ValidationReport>> GetReportsAsync(string chapterId);

        Task<ValidationReport?> GetLatestReportAsync(string chapterId);

        Task SaveReportAsync(ValidationReport report);

        Task DeleteReportAsync(string reportId);

        Task<Dictionary<string, ValidationResult>> GetAllChapterStatusAsync();
    }
}
