using System.Collections.Generic;
using System.Linq;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Models
{
    public class RefineryResult
    {
        public string Name { get; set; } = string.Empty;

        public RefineryModuleType TargetModule { get; set; }

        public Dictionary<string, string> Fields { get; set; } = new();

        public bool IsValid { get; set; } = true;

        public string ValidationMessage { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public List<KeyValuePair<string, string>> DisplayFields
        {
            get
            {
                var priority = GetDisplayPriority(TargetModule);
                return priority
                    .Where(p => Fields.TryGetValue(p.Key, out var v) && !string.IsNullOrWhiteSpace(v))
                    .Take(2)
                    .Select(p => new KeyValuePair<string, string>(p.Label, Fields[p.Key]))
                    .ToList();
            }
        }

        private static List<(string Key, string Label)> GetDisplayPriority(RefineryModuleType module) => module switch
        {
            RefineryModuleType.CharacterRules => new()
            {
                ("Gender", "性别"),
                ("CharacterType", "角色定位"),
                ("Identity", "身份"),
            },
            RefineryModuleType.FactionRules => new()
            {
                ("FactionType", "势力类型"),
                ("Leader", "首领"),
            },
            RefineryModuleType.LocationRules => new()
            {
                ("LocationType", "位置类型"),
                ("Description", "描述"),
            },
            RefineryModuleType.WorldRules => new()
            {
                ("OneLineSummary", "概述"),
                ("PowerSystem", "力量体系"),
            },
            RefineryModuleType.PlotRules => new()
            {
                ("EventType", "剧情类型"),
                ("OneLineSummary", "概述"),
            },
            _ => new()
        };
    }
}
