using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _rulesVersion;
        private IReadOnlyList<ProxyRule>? _cachedEnabledRules;

        private void InvalidateRuleCache() => _cachedEnabledRules = null;

        private static readonly ConcurrentDictionary<string, Lazy<Regex>> _regexCache = new();

        private static Regex GetOrAddRegex(string pattern)
            => _regexCache.GetOrAdd(pattern, p => new Lazy<Regex>(() => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))).Value;

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

        private static readonly string BuiltInIdPrefix = "BUILTIN_";

        private static readonly List<ProxyRule> _builtInRules = new()
        {
            new ProxyRule
            {
                Id = "BUILTIN_SELF_SERVER",
                Type = RuleType.Domain,
                Pattern = "zyzmczmc.xyz",
                Action = ProxyAction.Direct,
                Priority = -1000,
                Enabled = true,
                Description = "[内置] 程序服务器直连"
            },
            new ProxyRule
            {
                Id = "BUILTIN_ALIYUN",
                Type = RuleType.Wildcard,
                Pattern = "*.aliyuncs.com",
                Action = ProxyAction.Direct,
                Priority = -999,
                Enabled = true,
                Description = "[内置] 阿里云服务直连"
            },
            new ProxyRule
            {
                Id = "BUILTIN_LOCALHOST",
                Type = RuleType.Domain,
                Pattern = "localhost",
                Action = ProxyAction.Direct,
                Priority = -998,
                Enabled = true,
                Description = "[内置] 本地服务直连"
            },
            new ProxyRule
            {
                Id = "BUILTIN_LOCAL_IP",
                Type = RuleType.Wildcard,
                Pattern = "127.*",
                Action = ProxyAction.Direct,
                Priority = -997,
                Enabled = true,
                Description = "[内置] 本地IP直连"
            },
        };

        public ProxyRuleService()
        {
            _rulesFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy", "proxy_rules.json");
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await LoadRulesAsync().ConfigureAwait(false);
                    EnsureBuiltInRules();
                }
                catch (Exception ex) { TM.App.Log($"[ProxyRuleService] 初始化失败: {ex.Message}"); }
            });
        }

        public static bool IsBuiltInRule(string ruleId) => ruleId.StartsWith(BuiltInIdPrefix);

        public List<ProxyRule> GetRules() => new List<ProxyRule>(_rules.OrderBy(r => r.Priority));

        public List<ProxyRule> GetBuiltInRules() => new List<ProxyRule>(_builtInRules);

        public void AddRule(ProxyRule rule)
        {
            rule.Priority = _rules.Any() ? _rules.Max(r => r.Priority) + 1 : 1;
            _rules.Add(rule);
            System.Threading.Interlocked.Increment(ref _rulesVersion);
            InvalidateRuleCache();
            _ = SaveRulesAsync();
        }

        public void UpdateRule(ProxyRule rule)
        {
            if (IsBuiltInRule(rule.Id)) return;
            var index = _rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0)
            {
                _rules[index] = rule;
                System.Threading.Interlocked.Increment(ref _rulesVersion);
                InvalidateRuleCache();
                _ = SaveRulesAsync();
            }
        }

        public void DeleteRule(string ruleId)
        {
            if (IsBuiltInRule(ruleId)) return;
            _rules.RemoveAll(r => r.Id == ruleId);
            System.Threading.Interlocked.Increment(ref _rulesVersion);
            InvalidateRuleCache();
            ReorderPriorities();
            _ = SaveRulesAsync();
        }

        public void ToggleRule(string ruleId, bool enabled)
        {
            if (IsBuiltInRule(ruleId)) return;
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null)
            {
                rule.Enabled = enabled;
                System.Threading.Interlocked.Increment(ref _rulesVersion);
                InvalidateRuleCache();
                _ = SaveRulesAsync();
            }
        }

        public void MovePriority(string ruleId, bool moveUp)
        {
            if (IsBuiltInRule(ruleId)) return;
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            var sortedRules = _rules.OrderBy(r => r.Priority).ToList();
            var index = sortedRules.IndexOf(rule);

            if (moveUp && index > 0)
            {
                var neighbor = sortedRules[index - 1];
                if (IsBuiltInRule(neighbor.Id)) return;
                var temp = neighbor.Priority;
                neighbor.Priority = rule.Priority;
                rule.Priority = temp;
            }
            else if (!moveUp && index < sortedRules.Count - 1)
            {
                var neighbor = sortedRules[index + 1];
                if (IsBuiltInRule(neighbor.Id)) return;
                var temp = neighbor.Priority;
                neighbor.Priority = rule.Priority;
                rule.Priority = temp;
            }

            InvalidateRuleCache();
            _ = SaveRulesAsync();
        }

        public ProxyAction? MatchRule(string target)
        {
            return MatchRuleDetail(target)?.Action;
        }

        public ProxyRule? MatchRuleDetail(string target)
        {
            var enabledRules = _cachedEnabledRules ??=
                _rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();

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
                        return GetOrAddRegex(regexPattern).IsMatch(target);

                    case RuleType.Regex:
                        return GetOrAddRegex(rule.Pattern).IsMatch(target);

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

        public async System.Threading.Tasks.Task ImportRulesAsync(string filePath, bool append = false)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var importedRules = JsonSerializer.Deserialize<List<ProxyRule>>(json);

                if (importedRules != null)
                {
                    if (!append) _rules.Clear();
                    foreach (var rule in importedRules)
                        rule.Id = ShortIdGenerator.New("D");
                    _rules.AddRange(importedRules);
                    ReorderPriorities();
                    EnsureBuiltInRules();
                    InvalidateRuleCache();
                    await SaveRulesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 导入规则失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task ExportRulesAsync(string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(_rules, JsonHelper.CnDefault);
                var tmpEx = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpEx, json).ConfigureAwait(false);
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
            EnsureBuiltInRules();
            InvalidateRuleCache();
            _ = SaveRulesAsync();
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
            int userIndex = 1;
            foreach (var r in sortedRules)
            {
                if (!IsBuiltInRule(r.Id))
                {
                    r.Priority = userIndex++;
                }
            }
        }

        private async System.Threading.Tasks.Task LoadRulesAsync()
        {
            var loadVersion = System.Threading.Volatile.Read(ref _rulesVersion);
            try
            {
                if (File.Exists(_rulesFile))
                {
                    var json = await File.ReadAllTextAsync(_rulesFile).ConfigureAwait(false);
                    var rules = JsonSerializer.Deserialize<List<ProxyRule>>(json);
                    if (rules != null)
                    {
                        if (loadVersion != System.Threading.Volatile.Read(ref _rulesVersion))
                            return;
                        _rules = rules;
                        InvalidateRuleCache();
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 异步加载规则失败: {ex.Message}");
            }
        }

        private void EnsureBuiltInRules()
        {
            var existingBuiltInIds = new HashSet<string>(_rules.Where(r => IsBuiltInRule(r.Id)).Select(r => r.Id));
            bool changed = false;

            foreach (var builtIn in _builtInRules)
            {
                if (!existingBuiltInIds.Contains(builtIn.Id))
                {
                    _rules.Add(new ProxyRule
                    {
                        Id = builtIn.Id,
                        Type = builtIn.Type,
                        Pattern = builtIn.Pattern,
                        Action = builtIn.Action,
                        Priority = builtIn.Priority,
                        Enabled = builtIn.Enabled,
                        Description = builtIn.Description
                    });
                    changed = true;
                }
                else
                {
                    var existing = _rules.First(r => r.Id == builtIn.Id);
                    if (existing.Pattern != builtIn.Pattern || existing.Action != builtIn.Action ||
                        existing.Type != builtIn.Type || !existing.Enabled || existing.Priority != builtIn.Priority)
                    {
                        existing.Pattern = builtIn.Pattern;
                        existing.Action = builtIn.Action;
                        existing.Type = builtIn.Type;
                        existing.Description = builtIn.Description;
                        existing.Priority = builtIn.Priority;
                        existing.Enabled = true;
                        changed = true;
                    }
                }
            }

            _rules.RemoveAll(r => IsBuiltInRule(r.Id) && !_builtInRules.Any(b => b.Id == r.Id));

            if (changed)
            {
                InvalidateRuleCache();
                _ = SaveRulesAsync();
            }
        }

        private async System.Threading.Tasks.Task SaveRulesAsync()
        {
            var json = JsonSerializer.Serialize(_rules, JsonHelper.CnDefault);
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var tmpRa = _rulesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpRa, json).ConfigureAwait(false);
                File.Move(tmpRa, _rulesFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRuleService] 异步保存规则失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}

