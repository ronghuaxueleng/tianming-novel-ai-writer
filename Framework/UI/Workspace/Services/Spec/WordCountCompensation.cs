using System;
using System.Collections.Generic;

namespace TM.Framework.UI.Workspace.Services.Spec
{
    public static class WordCountCompensation
    {
        private static readonly List<(string Pattern, double Ratio, string Label)> CompensationTable = new()
        {
            ("gpt",       0.70, "GPT"),
            ("deepseek",  0.70, "DeepSeek"),
        };

        public const double DefaultRatio = 1.0;

        public static double GetRatio(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return DefaultRatio;

            foreach (var (pattern, ratio, _) in CompensationTable)
            {
                if (modelId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return ratio;
            }

            return DefaultRatio;
        }

        public static string GetLabel(string? modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return "未知模型";

            foreach (var (pattern, _, label) in CompensationTable)
            {
                if (modelId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return label;
            }

            return "未知模型";
        }

        public static int GetAdjustedTarget(int originalTarget, string? modelId)
        {
            return (int)(originalTarget * GetRatio(modelId));
        }
    }
}
