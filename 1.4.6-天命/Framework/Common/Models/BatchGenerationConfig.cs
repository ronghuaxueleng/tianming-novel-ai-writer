using System;

namespace TM.Framework.Common.Models
{
    public class BatchGenerationConfig
    {
        public string CategoryName { get; set; } = string.Empty;

        public int TotalCount { get; set; } = 10;

        public int BatchSize { get; set; } = 15;

        public int EstimatedBatches => (int)Math.Ceiling((double)TotalCount / BatchSize);
    }
}
