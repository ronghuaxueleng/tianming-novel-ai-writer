using System.Collections.Generic;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Models
{
    public class RefineryWorkState
    {
        public RefineryModuleType? SelectedModuleType { get; set; }

        public string RawContent { get; set; } = string.Empty;

        public string PrerequisiteValue { get; set; } = string.Empty;

        public List<RefineryResult> PendingResults { get; set; } = new();
    }
}
