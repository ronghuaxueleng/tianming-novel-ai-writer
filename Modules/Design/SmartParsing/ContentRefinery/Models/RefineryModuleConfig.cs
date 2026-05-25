using System.Collections.Generic;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Models
{
    public class RefineryModuleConfig
    {
        public RefineryModuleType ModuleType { get; set; }

        public AIGenerationConfig AIConfig { get; set; } = new();

        public List<RefineryRequiredInput> RequiredInputs { get; set; } = new();
    }
}
