using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.SystemSettings.Proxy.Services
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum RuleType
    {
        Domain,
        IP,
        Wildcard,
        Regex
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ProxyAction
    {
        Direct,
        Proxy,
        Block
    }

    public class ProxyRule
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public RuleType Type { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Pattern")] public string Pattern { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Action")] public ProxyAction Action { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Priority")] public int Priority { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    }

    public class ProxyRuleService
    {

        private readonly string _rulesFile;
        private List<ProxyRule> _rules = new();

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[ProxyRuleService] {key}: {ex.Message}");
        }

        public ProxyRuleService()
        {
            _rulesFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy", "proxy_rules.json");
            LoadRules();
        }

        public List<ProxyRule> GetRules() => new List<ProxyRule>(_rules.OrderBy(r => r.Priority));

        public void AddRule(ProxyRule rule)
        {
            rule.Priority = _rules.Any() ? _rules.Max(r => r.Priority) + 1 : 1;
            _rules.Add(rule);
            SaveRules();
        }

        public void UpdateRule(ProxyRule rule)
        {
            var index = _rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0)
            {
                _rules[index] = rule;
                SaveRules();
            }
        }

        public void DeleteRule(string ruleId)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
            ReorderPriorities();
            SaveRules();
        }

        public void ToggleRule(string ruleId, bool enabled)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.Enabled = enabled;
                SaveRules();
            }
        }

        public void MovePriority(string ruleId, bool moveUp)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            var sortedRules = _rules.OrderBy(r => r.Priority).ToList();
            var index = sortedRules.IndexOf(rule);

            if (moveUp && index > 0)
            {
                var temp = sortedRules[index - 1].Priority;
                sortedRules[index - 1].Priority = rule.Priority;
                rule.Priority = temp;
            }
            else if (!moveUp && index < sortedRules.Count - 1)
            {
                var temp = sortedRules[index + 1].Priority;
                sortedRules[index + 1].Priority = rule.Priority;
                rule.Priority = temp;
            }

            SaveRules();
        }

        public ProxyAction? MatchRule(string target)
        {
            return MatchRuleDetail(target)?.Action;
        }

        public ProxyRule? MatchRuleDetail(string target)
        {
            var enabledRules = _rules.Where(r => r.Enabled).OrderBy(r => r.Priority);

            foreach (var rule in enabledRules)
            {
                if (IsMatch(rule, target))
                {
                    return rule;
                }
            }

            return null;
        }

        private bool IsMatch(ProxyRule rule, string target)
        {
            try
            {
                switch (rule.Type)
                {
                    case RuleType.Domain:
                        return target.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase) ||
                               target.EndsWith("." + rule.Pattern, StringComparison.OrdinalIgnoreCase);

                    case RuleType.IP:
                        return target == rule.Pattern;

                    case RuleType.Wildcard:
                        var regexPattern = "^" + Regex.Escape(rule.Pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                        return Regex.IsMatch(target, regexPattern, RegexOptions.IgnoreCase);

                    case RuleType.Regex:
                        return Regex.IsMatch(target, rule.Pattern, RegexOptions.IgnoreCase);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(IsMatch), ex);
                return false;
            }
        }

        public void ImportRules(string filePath, bool append = false)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var importedRules = JsonSerializer.Deserialize<List<ProxyRule>>(json);

                if (importedRules != null)
                {
                    if (!append)
                    {
                        _rules.Clear();
                    }

                    foreach (var rule in importedRules)
                    {
                        rule.Id = ShortIdGenerator.New("D");
                    }

                    _rules.AddRange(importedRules);
                    ReorderPriorities();
                    SaveRules();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 导入规则失败: {ex.Message}");
                throw;
            }
        }

        public void ExportRules(string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_rules, JsonHelper.CnDefault);
                var tmpEx = filePath + ".tmp";
                File.WriteAllText(tmpEx, json);
                File.Move(tmpEx, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 导出规则失败: {ex.Message}");
                throw;
            }
        }

        public void LoadPresetTemplate(string templateName)
        {
            var presetRules = new List<ProxyRule>();

            switch (templateName)
            {
                case "ad_block":
                    presetRules.AddRange(GetAdBlockRules());
                    break;
                case "china_direct":
                    presetRules.AddRange(GetChinaDirectRules());
                    break;
                case "foreign_proxy":
                    presetRules.AddRange(GetForeignProxyRules());
                    break;
            }

            _rules.AddRange(presetRules);
            ReorderPriorities();
            SaveRules();
        }

        private List<ProxyRule> GetAdBlockRules()
        {
            return new List<ProxyRule>
            {
                new ProxyRule { Type = RuleType.Domain, Pattern = "doubleclick.net", Action = ProxyAction.Block, Description = "Google广告" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "googlesyndication.com", Action = ProxyAction.Block, Description = "Google广告联盟" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "googleadservices.com", Action = ProxyAction.Block, Description = "Google广告服务" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "adnxs.com", Action = ProxyAction.Block, Description = "AppNexus广告" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "adsrvr.org", Action = ProxyAction.Block, Description = "TradeDesk广告" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "adcolony.com", Action = ProxyAction.Block, Description = "AdColony广告" },
                new ProxyRule { Type = RuleType.Wildcard, Pattern = "ads.*.com", Action = ProxyAction.Block, Description = "ads子域名屏蔽" },
                new ProxyRule { Type = RuleType.Wildcard, Pattern = "ad.*.com", Action = ProxyAction.Block, Description = "ad子域名屏蔽" },
                new ProxyRule { Type = RuleType.Wildcard, Pattern = "*.adserver.*", Action = ProxyAction.Block, Description = "广告服务器屏蔽" }
            };
        }

        private List<ProxyRule> GetChinaDirectRules()
        {
            return new List<ProxyRule>
            {
                new ProxyRule { Type = RuleType.Wildcard, Pattern = "*.cn", Action = ProxyAction.Direct, Description = "中国域名直连" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "baidu.com", Action = ProxyAction.Direct, Description = "百度直连" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "qq.com", Action = ProxyAction.Direct, Description = "腾讯直连" }
            };
        }

        private List<ProxyRule> GetForeignProxyRules()
        {
            return new List<ProxyRule>
            {
                new ProxyRule { Type = RuleType.Domain, Pattern = "google.com", Action = ProxyAction.Proxy, Description = "Google代理" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "youtube.com", Action = ProxyAction.Proxy, Description = "YouTube代理" },
                new ProxyRule { Type = RuleType.Domain, Pattern = "twitter.com", Action = ProxyAction.Proxy, Description = "Twitter代理" }
            };
        }

        private void ReorderPriorities()
        {
            var sortedRules = _rules.OrderBy(r => r.Priority).ToList();
            for (int i = 0; i < sortedRules.Count; i++)
            {
                sortedRules[i].Priority = i + 1;
            }
        }

        private void LoadRules()
        {
            try
            {
                if (File.Exists(_rulesFile))
                {
                    var json = File.ReadAllText(_rulesFile);
                    var rules = JsonSerializer.Deserialize<List<ProxyRule>>(json);
                    if (rules != null)
                    {
                        _rules = rules;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 加载规则失败: {ex.Message}");
            }
        }

        private void SaveRules()
        {
            try
            {
                var json = JsonSerializer.Serialize(_rules, JsonHelper.CnDefault);
                var tmpR = _rulesFile + ".tmp";
                File.WriteAllText(tmpR, json);
                File.Move(tmpR, _rulesFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 保存规则失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveRulesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_rules, JsonHelper.CnDefault);
                var tmpRa = _rulesFile + ".tmp";
                await File.WriteAllTextAsync(tmpRa, json);
                File.Move(tmpRa, _rulesFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 异步保存规则失败: {ex.Message}");
            }
        }
    }
}

