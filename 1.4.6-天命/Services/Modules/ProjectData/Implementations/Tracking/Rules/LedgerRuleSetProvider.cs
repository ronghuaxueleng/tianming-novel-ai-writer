using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations.Tracking.Rules
{
    public sealed class LedgerRuleSetProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public LedgerRuleSet GetRuleSetForGate()
        {
            try
            {
                var genre = TryResolveGenreFromEnabledCreativeMaterials();
                return BuildRuleSetByGenre(genre);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LedgerRuleSetProvider] 获取规则集失败，回退通用规则: {ex.Message}");
                return LedgerRuleSet.CreateUniversalDefault();
            }
        }

        private static string? TryResolveGenreFromEnabledCreativeMaterials()
        {
            var path = StoragePathHelper.GetFilePath(
                "Modules",
                "Design/Templates/CreativeMaterials",
                "creative_materials.json");

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var items = JsonSerializer.Deserialize<List<CreativeMaterialData>>(json, JsonOptions) ?? new();

                var enabled = items
                    .Where(i => i != null && i.IsEnabled)
                    .OrderByDescending(i => i!.ModifiedTime)
                    .FirstOrDefault();

                var genre = enabled?.Genre;
                return string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LedgerRuleSetProvider] 读取创作模板题材失败，回退通用规则: {ex.Message}");
                return null;
            }
        }

        private static LedgerRuleSet BuildRuleSetByGenre(string? genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return LedgerRuleSet.CreateUniversalDefault();
            }

            var g = genre.Trim();

            if (ContainsAny(g, "玄幻", "奇幻", "仙侠", "武侠") )
            {
                var ruleSet = LedgerRuleSet.CreateUniversalDefault();
                ruleSet.EnableAbilityLossRequiresEvent = true;
                ruleSet.AbilityLossKeywords = new List<string>
                {
                    "失去", "封印", "封禁", "剥夺", "退化", "降级", "丧失", "消散", "废功", "废除", "失效"
                };
                return ruleSet;
            }

            if (ContainsAny(g, "都市", "现实") )
            {
                var ruleSet = LedgerRuleSet.CreateUniversalDefault();
                ruleSet.EnableAbilityLossRequiresEvent = true;
                ruleSet.AbilityLossKeywords = new List<string>
                {
                    "失去", "封号", "停职", "冻结", "权限回收", "撤职", "取消资格", "禁用", "作废", "失效", "剥夺", "降级"
                };
                return ruleSet;
            }

            return LedgerRuleSet.CreateUniversalDefault();
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            foreach (var k in keywords)
            {
                if (string.IsNullOrWhiteSpace(k))
                {
                    continue;
                }

                if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
