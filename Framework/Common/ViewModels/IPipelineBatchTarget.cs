using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Framework.Common.ViewModels
{
    public interface IPipelineBatchTarget
    {
        string PipelineModuleName { get; }

        bool IsBatchGenerating { get; }

        List<string> GetCategoryNames();

        bool IsPipelineSingleMode { get; }

        int GetPipelineDefaultCount();

        Dictionary<string, string> GetPrefilledFieldDefaults(string categoryName);

        Dictionary<string, List<string>> GetExtraFieldOptions();

        Task<List<string>> GetIncompletePrerequisiteCategoriesAsync(string categoryName);

        bool ConfirmAndEndAISessionForPipeline();

        void ForceRefreshTreeData();

        Task<PipelineBatchResult> ExecutePipelineBatchAsync(
            PipelineBatchRequest request,
            IProgress<string>? progress,
            CancellationToken cancellationToken);
    }

    public class PipelineBatchRequest
    {
        public string CategoryName { get; set; } = string.Empty;

        public int Count { get; set; }

        public Dictionary<string, string> PrefilledFields { get; set; } = new();

        public bool IsResumeMode { get; set; }
    }

    public class PipelineBatchResult
    {
        public bool Success { get; set; }
        public int GeneratedCount { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
