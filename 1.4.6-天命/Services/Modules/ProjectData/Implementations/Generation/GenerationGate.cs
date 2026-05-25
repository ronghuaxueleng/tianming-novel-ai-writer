using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class GenerationGate
    {
        private readonly LedgerConsistencyChecker _ledgerConsistencyChecker;
        private readonly LedgerRuleSetProvider _ledgerRuleSetProvider;

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
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[GenerationGate] {key}: {ex.Message}");
        }

        #region 构造函数

        public GenerationGate(LedgerConsistencyChecker ledgerConsistencyChecker, LedgerRuleSetProvider ledgerRuleSetProvider)
        {
            _ledgerConsistencyChecker = ledgerConsistencyChecker;
            _ledgerRuleSetProvider = ledgerRuleSetProvider;
        }

        #endregion

        #region 常量

        public const string ChangesSeparator = "---CHANGES---";

        internal static readonly Regex ChangesSeparatorLineRegex = new(
            @"(?m)^\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*CHANGES\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static readonly Regex MdChangesHeaderRegex = new(
            @"(?m)^(?:---\s*\n+\s*)?#{1,3}\s*(?:CHANGES|变更记录|变更摘要|状态变更)\s*\n",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] ChangesSignatureFields = 
        {
            "CharacterStateChanges", "ConflictProgress", "ForeshadowingActions",
            "NewPlotPoints", "LocationStateChanges", "FactionStateChanges",
            "TimeProgression", "CharacterMovements", "ItemTransfers"
        };

        #endregion

        #region 公开方法

        public ConsistencyResult ValidateStructuralOnly(ChapterChanges changes)
            => _ledgerConsistencyChecker.ValidateStructuralOnly(changes);

        public async Task<GateResult> ValidateAsync(
            string chapterId,
            string rawContent,
            FactSnapshot factSnapshot,
            DesignElementNames? designElements = null)
        {
            var result = new GateResult { ChapterId = chapterId };

            TM.App.Log($"[GG] start: {chapterId}");

            var protocolResult = ValidateChangesProtocol(rawContent);
            if (!protocolResult.Success)
            {
                result.AddFailure(FailureType.Protocol, protocolResult.Errors);
                TM.App.Log("[GG] fail");
                return result;
            }

            result.ParsedChanges = protocolResult.Changes;
            result.ContentWithoutChanges = protocolResult.ContentWithoutChanges;

            var ruleSet = _ledgerRuleSetProvider.GetRuleSetForGate();
            var consistencyResult = _ledgerConsistencyChecker.Validate(
                protocolResult.Changes!, 
                factSnapshot,
                ruleSet);
            if (!consistencyResult.Success)
            {
                result.AddFailure(FailureType.Consistency, consistencyResult.GetIssueDescriptions());
                TM.App.Log("[GG] fail");
                return result;
            }

            var contentToValidate = protocolResult.ContentWithoutChanges ?? "";

            var step4Task = Task.Run(() =>
            {
                var entityExtractor = new ContentEntityExtractor(factSnapshot);
                return entityExtractor.GetUnknownEntities(contentToValidate);
            });

            var step5Task = Task.Run(() =>
            {
                var issues = new List<string>();
                if (factSnapshot.CharacterDescriptions.Count > 0 || factSnapshot.LocationDescriptions.Count > 0)
                {
                    var descValidator = new ContentDescriptionValidator();
                    issues.AddRange(descValidator.ValidateCharacterDescriptions(contentToValidate, factSnapshot.CharacterDescriptions));
                    issues.AddRange(descValidator.ValidateLocationDescriptions(contentToValidate, factSnapshot.LocationDescriptions));
                }
                return issues;
            });

            var step6Task = Task.Run(() =>
            {
                if (factSnapshot.WorldRuleConstraints.Count > 0)
                {
                    return ValidateWorldRuleConstraints(contentToValidate, factSnapshot.WorldRuleConstraints);
                }
                return new List<string>();
            });

            await Task.WhenAll(step4Task, step5Task, step6Task);

            var unknownEntities = await step4Task;
            var descIssues = await step5Task;
            var ruleViolations = await step6Task;

            if (unknownEntities.Count > 0)
            {
                var changes = protocolResult.Changes;
                var functionalEntities = unknownEntities
                    .Where(e => IsEntityInChanges(e, changes))
                    .ToList();
                var backgroundEntities = unknownEntities
                    .Where(e => !IsEntityInChanges(e, changes))
                    .ToList();

                if (unknownEntities.Count > 5 || backgroundEntities.Count > 3)
                {
                    var functionalSet = new HashSet<string>(functionalEntities, StringComparer.OrdinalIgnoreCase);
                    var failures = unknownEntities
                        .Select(e => functionalSet.Contains(e)
                            ? $"正文引入未登记实体(有剧情作用): {e}"
                            : $"正文引入未登记实体(龙套): {e}")
                        .ToList();
                    result.AddFailure(FailureType.Consistency, failures);
                    TM.App.Log($"[GG] fail: 未知实体超限 功能性{functionalEntities.Count}个 龙套{backgroundEntities.Count}个");
                    return result;
                }
                else
                {
                    TM.App.Log($"[GG] warn: 未知实体 功能性{functionalEntities.Count}个 龙套{backgroundEntities.Count}个（允许通过）");
                }
            }

            if (descIssues.Count > 0)
            {
                result.AddFailure(FailureType.Consistency, descIssues);
                TM.App.Log("[GG] fail");
                return result;
            }

            if (ruleViolations.Count > 0)
            {
                result.AddFailure(FailureType.Consistency, ruleViolations);
                TM.App.Log("[GG] fail");
                return result;
            }

            if (designElements != null)
            {
                var (povFailures, designIssues, qualityWarnings) = ValidateDesignElementPresence(contentToValidate, designElements);
                if (qualityWarnings.Count > 0)
                    TM.App.Log($"[GG] quality-warn: {string.Join("; ", qualityWarnings)}");
                if (povFailures.Count > 0)
                {
                    TM.App.Log($"[GG] quality-warn: POV角色缺席（可接受） {string.Join("; ", povFailures)}");
                }
                var totalElements = designElements.CharacterNames.Count
                                  + designElements.FactionNames.Count
                                  + designElements.LocationNames.Count
                                  + designElements.PlotKeyNames.Count;
                var threshold = Math.Max(3, totalElements / 3);
                if (designIssues.Count > threshold)
                {
                    result.AddFailure(FailureType.Consistency, designIssues);
                    TM.App.Log($"[GG] fail: design {designIssues.Count}/{totalElements} missing (threshold={threshold})");
                    return result;
                }
                else if (designIssues.Count > 0)
                {
                    TM.App.Log($"[GG] warn: design {designIssues.Count}/{totalElements} missing (threshold={threshold}, pass)");
                }
            }

            result.Success = true;
            TM.App.Log($"[GG] ok: {chapterId}");
            return result;
        }

        private static (List<string> povFailures, List<string> issues, List<string> qualityWarnings) ValidateDesignElementPresence(string content, DesignElementNames elements)
        {
            var povFailures = new List<string>();
            var issues = new List<string>();
            var qualityWarnings = new List<string>();

            foreach (var name in elements.PovCharacterNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    povFailures.Add($"视角角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"视角角色'{name}'仅出现1次，叙事力度不足");
            }

            foreach (var name in elements.CharacterNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"角色'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.FactionNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定势力未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"势力'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.LocationNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"指定地点未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"地点'{name}'仅出现1次，疑似背景一提");
            }

            foreach (var name in elements.PlotKeyNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var count = CountNameInContent(content, name);
                if (count == 0)
                    issues.Add($"剧情关键角色未在正文出现: {name}");
                else if (count == 1)
                    qualityWarnings.Add($"剧情关键角色'{name}'仅出现1次，疑似背景一提");
            }

            return (povFailures, issues, qualityWarnings);
        }

        private static int CountNameInContent(string content, string fullName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(fullName))
                return 0;

            var variants = new List<string>();
            void AddVariant(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var v = s.Trim();
                if (v.Length < 2) return;
                if (!variants.Contains(v)) variants.Add(v);
            }

            AddVariant(fullName);

            var primaryName = EntityNameNormalizeHelper.StripBracketAnnotation(fullName);
            if (!string.IsNullOrWhiteSpace(primaryName) && !string.Equals(primaryName, fullName, StringComparison.Ordinal))
                AddVariant(primaryName);

            var aliasMatches = Regex.Matches(fullName, @"[\(（\[【](.+?)[\)）\]】]");
            foreach (Match m in aliasMatches)
                AddVariant(m.Groups[1].Value);

            var maxCount = 0;
            foreach (var nameToCount in variants)
            {
                var count = 0;
                var idx = 0;
                while ((idx = content.IndexOf(nameToCount, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    count++;
                    idx += nameToCount.Length;
                }
                if (count > maxCount) maxCount = count;
            }

            return maxCount;
        }

        private static bool DesignNameExistsInContent(string content, string fullName)
        {
            return EntityNameNormalizeHelper.NameExistsInContent(content, fullName);
        }

        private static bool IsEntityInChanges(string entityName, ChapterChanges? changes)
        {
            if (changes == null || string.IsNullOrWhiteSpace(entityName))
                return false;

            var name = entityName.Trim();

            foreach (var c in changes.CharacterStateChanges ?? new())
                if (ContainsEntityName(c.CharacterId, name) || ContainsEntityName(c.KeyEvent, name))
                    return true;

            foreach (var c in changes.ConflictProgress ?? new())
                if (ContainsEntityName(c.ConflictId, name) || ContainsEntityName(c.Event, name))
                    return true;

            foreach (var f in changes.ForeshadowingActions ?? new())
                if (ContainsEntityName(f.ForeshadowId, name))
                    return true;

            foreach (var p in changes.NewPlotPoints ?? new())
                if (ContainsEntityName(p.Context, name) ||
                    (p.InvolvedCharacters?.Any(ic => ContainsEntityName(ic, name)) == true))
                    return true;

            foreach (var l in changes.LocationStateChanges ?? new())
                if (ContainsEntityName(l.LocationId, name) || ContainsEntityName(l.Event, name))
                    return true;

            foreach (var fa in changes.FactionStateChanges ?? new())
                if (ContainsEntityName(fa.FactionId, name) || ContainsEntityName(fa.Event, name))
                    return true;

            foreach (var m in changes.CharacterMovements ?? new())
                if (ContainsEntityName(m.CharacterId, name))
                    return true;

            foreach (var it in changes.ItemTransfers ?? new())
                if (ContainsEntityName(it.ItemName, name) || ContainsEntityName(it.Event, name))
                    return true;

            return false;
        }

        private static bool ContainsEntityName(string? text, string entityName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(entityName))
                return false;
            return text.Contains(entityName, StringComparison.OrdinalIgnoreCase);
        }

        public ProtocolValidationResult ValidateChangesProtocol(string rawContent)
        {
            var result = new ProtocolValidationResult();

            if (string.IsNullOrEmpty(rawContent))
            {
                result.AddError("内容为空");
                return result;
            }

            var (contentPart, changesPart, formatType) = IdentifyChangesRegion(rawContent);

            if (changesPart == null)
            {
                result.AddError($"未识别到CHANGES区域（支持格式：{ChangesSeparator}、末尾JSON）");
                return result;
            }

            result.ContentWithoutChanges = contentPart;
            TM.App.Log($"[GG] 识别到CHANGES区域，格式: {formatType}");

            var (changes, parseError) = ParseChangesContent(changesPart, formatType);

            if (changes == null)
            {
                result.AddError(parseError ?? "CHANGES解析失败");
                return result;
            }

            result.Changes = changes;

            if (formatType == ChangesFormatType.Json || formatType == ChangesFormatType.JsonNoSeparator)
            {
                var jsonStr = ExtractJsonFromChangesSection(changesPart);
                if (!string.IsNullOrEmpty(jsonStr))
                {
                    var repairedJsonStr = RepairChangesJson(jsonStr);
                    ValidateRequiredFields(result, repairedJsonStr);
                }
            }

            if (!result.HasErrors)
            {
                ValidateShortIdFields(result);
            }

            result.Success = !result.HasErrors;
            return result;
        }

        private enum ChangesFormatType
        {
            Json,
            JsonNoSeparator,
            Markdown
        }

        private (string content, string? changes, ChangesFormatType format) IdentifyChangesRegion(string rawContent)
        {
            var idx = rawContent.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return (
                    rawContent.Substring(0, idx).Trim(),
                    rawContent.Substring(idx + ChangesSeparator.Length).Trim(),
                    ChangesFormatType.Json
                );
            }

            var m = ChangesSeparatorLineRegex.Match(rawContent);
            if (m.Success)
            {
                return (
                    rawContent.Substring(0, m.Index).Trim(),
                    rawContent.Substring(m.Index + m.Length).Trim(),
                    ChangesFormatType.Json
                );
            }

            var mdMatch = MdChangesHeaderRegex.Match(rawContent);
            if (mdMatch.Success)
            {
                return (
                    rawContent.Substring(0, mdMatch.Index).Trim(),
                    rawContent.Substring(mdMatch.Index).Trim(),
                    ChangesFormatType.Markdown
                );
            }

            var jsonResult = TryIdentifyTrailingJson(rawContent);
            if (jsonResult.HasValue)
            {
                return (
                    rawContent.Substring(0, jsonResult.Value.startIndex).Trim(),
                    jsonResult.Value.json,
                    ChangesFormatType.JsonNoSeparator
                );
            }

            var mdContentMatch = Regex.Match(rawContent, @"(?m)^---\s*\n+\s*\*\*[^*]+\*\*[：:]");
            if (mdContentMatch.Success)
            {
                return (
                    rawContent.Substring(0, mdContentMatch.Index).Trim(),
                    rawContent.Substring(mdContentMatch.Index).Trim(),
                    ChangesFormatType.Markdown
                );
            }

            return (rawContent, null, ChangesFormatType.Json);
        }

        private (int startIndex, string json)? TryIdentifyTrailingJson(string rawContent)
        {
            var lastBrace = rawContent.LastIndexOf('}');
            if (lastBrace < 0) return null;

            var braceCount = 0;
            var jsonStartIndex = -1;

            for (var i = lastBrace; i >= 0; i--)
            {
                var c = rawContent[i];
                if (c == '}') braceCount++;
                else if (c == '{')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        jsonStartIndex = i;
                        break;
                    }
                }
            }

            if (jsonStartIndex < 0) return null;

            var candidateJson = rawContent.Substring(jsonStartIndex, lastBrace - jsonStartIndex + 1);

            try
            {
                using var doc = JsonDocument.Parse(candidateJson, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                var matchedFields = 0;
                foreach (var field in ChangesSignatureFields)
                {
                    if (doc.RootElement.TryGetProperty(field, out _) ||
                        doc.RootElement.TryGetProperty(ToCamelCase(field), out _))
                    {
                        matchedFields++;
                    }
                }

                if (matchedFields < 2) return null;

                var actualStart = jsonStartIndex;
                var beforeJson = rawContent.Substring(0, jsonStartIndex).TrimEnd();
                var codeBlockIdx = beforeJson.LastIndexOf("```", StringComparison.Ordinal);
                if (codeBlockIdx >= 0)
                {
                    var between = beforeJson.Substring(codeBlockIdx + 3).Trim();
                    if (string.IsNullOrEmpty(between) || between.Equals("json", StringComparison.OrdinalIgnoreCase))
                    {
                        var lineStart = beforeJson.LastIndexOf('\n', codeBlockIdx);
                        actualStart = lineStart >= 0 ? lineStart : codeBlockIdx;
                    }
                }

                return (actualStart, candidateJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ToCamelCase(string s) => 
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        private (ChapterChanges? changes, string? error) ParseChangesContent(string changesPart, ChangesFormatType formatType)
        {
            if (formatType == ChangesFormatType.Markdown)
            {
                return (null, "CHANGES只允许JSON格式（### CHANGES 等 Markdown 格式不被接受）。请确保正文末尾输出 ---CHANGES--- 分隔符并紧跟JSON对象。");
            }

            var jsonStr = ExtractJsonFromChangesSection(changesPart);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                var jsonResult = TryParseJsonChanges(jsonStr);
                if (jsonResult != null)
                {
                    return (jsonResult, null);
                }
            }

            return (null, "CHANGES解析失败：JSON格式无法解析，请检查JSON语法（括号、逗号、引号）");
        }

        private ChapterChanges? TryParseJsonChanges(string jsonStr)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                options.Converters.Add(new CharacterStateChangeConverter());

                try
                {
                    return JsonSerializer.Deserialize<ChapterChanges>(jsonStr, options);
                }
                catch (JsonException)
                {
                    var repaired = RepairChangesJson(jsonStr);
                    if (!string.Equals(repaired, jsonStr, StringComparison.Ordinal))
                    {
                        TM.App.Log("[GG] JSON修复后重试");
                        return JsonSerializer.Deserialize<ChapterChanges>(repaired, options);
                    }
                    throw;
                }
            }
            catch (JsonException ex)
            {
                TM.App.Log($"[GG] JSON解析失败: {ex.Message}");
                return null;
            }
        }

        private sealed class CharacterStateChangeConverter : System.Text.Json.Serialization.JsonConverter<CharacterStateChange>
        {
            public override CharacterStateChange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new CharacterStateChange();
                }

                var model = new CharacterStateChange();

                if (TryGetString(root, "CharacterId", out var characterId) || TryGetString(root, "characterId", out characterId))
                {
                    model.CharacterId = characterId;
                }
                else if (TryGetString(root, "Character", out var character) || TryGetString(root, "character", out character))
                {
                    model.CharacterId = character;
                }

                if (TryGetString(root, "NewLevel", out var newLevel) || TryGetString(root, "newLevel", out newLevel) || TryGetString(root, "level", out newLevel))
                {
                    model.NewLevel = newLevel;
                }

                if (TryGetString(root, "NewMentalState", out var mental) || TryGetString(root, "newMentalState", out mental) || TryGetString(root, "mentalState", out mental))
                {
                    model.NewMentalState = mental;
                }

                if (TryGetString(root, "KeyEvent", out var keyEvent) || TryGetString(root, "keyEvent", out keyEvent))
                {
                    model.KeyEvent = keyEvent;
                }
                else if (TryGetString(root, "change", out var altKeyEvent) || TryGetString(root, "Change", out altKeyEvent))
                {
                    model.KeyEvent = altKeyEvent;
                }

                if (root.TryGetProperty("NewAbilities", out var abilitiesEl) || root.TryGetProperty("newAbilities", out abilitiesEl) || root.TryGetProperty("abilities", out abilitiesEl))
                {
                    if (abilitiesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in abilitiesEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                model.NewAbilities.Add(item.GetString() ?? string.Empty);
                            }
                        }
                    }
                    else if (abilitiesEl.ValueKind == JsonValueKind.String)
                    {
                        var s = abilitiesEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            model.NewAbilities.Add(s);
                        }
                    }
                }

                if (root.TryGetProperty("LostAbilities", out var lostAbilitiesEl) || root.TryGetProperty("lostAbilities", out lostAbilitiesEl))
                {
                    if (lostAbilitiesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in lostAbilitiesEl.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                model.LostAbilities.Add(item.GetString() ?? string.Empty);
                            }
                        }
                    }
                    else if (lostAbilitiesEl.ValueKind == JsonValueKind.String)
                    {
                        var s = lostAbilitiesEl.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            model.LostAbilities.Add(s);
                        }
                    }
                }

                if (TryGetString(root, "Importance", out var importance) || TryGetString(root, "importance", out importance))
                {
                    model.Importance = importance;
                }

                if (root.TryGetProperty("RelationshipChanges", out var relEl) || root.TryGetProperty("relationshipChanges", out relEl))
                {
                    if (relEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in relEl.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Object)
                            {
                                var rc = new RelationshipChange();
                                if (TryGetString(prop.Value, "Relation", out var relation) || TryGetString(prop.Value, "relation", out relation))
                                {
                                    rc.Relation = relation;
                                }
                                if (TryGetInt(prop.Value, "TrustDelta", out var delta) || TryGetInt(prop.Value, "trustDelta", out delta))
                                {
                                    rc.TrustDelta = delta;
                                }
                                model.RelationshipChanges[prop.Name] = rc;
                            }
                        }
                    }
                }

                return model;
            }

            public override void Write(Utf8JsonWriter writer, CharacterStateChange value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("CharacterId", value.CharacterId);
                writer.WriteString("NewLevel", value.NewLevel);
                writer.WritePropertyName("NewAbilities");
                JsonSerializer.Serialize(writer, value.NewAbilities, options);
                writer.WritePropertyName("LostAbilities");
                JsonSerializer.Serialize(writer, value.LostAbilities, options);
                writer.WritePropertyName("RelationshipChanges");
                JsonSerializer.Serialize(writer, value.RelationshipChanges, options);
                writer.WriteString("NewMentalState", value.NewMentalState);
                writer.WriteString("KeyEvent", value.KeyEvent);
                writer.WriteString("Importance", value.Importance ?? "normal");
                writer.WriteEndObject();
            }

            private static bool TryGetString(JsonElement obj, string name, out string value)
            {
                if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    value = el.GetString() ?? string.Empty;
                    return true;
                }
                value = string.Empty;
                return false;
            }

            private static bool TryGetInt(JsonElement obj, string name, out int value)
            {
                if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el))
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
                    {
                        return true;
                    }
                    if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out value))
                    {
                        return true;
                    }
                }
                value = 0;
                return false;
            }
        }

        #endregion

        #region 私有方法

        private string ExtractJsonFromChangesSection(string changesSection)
        {
            if (string.IsNullOrEmpty(changesSection))
                return string.Empty;

            var content = changesSection.Trim();

            if (content.StartsWith("```"))
            {
                var firstNewline = content.IndexOf('\n');
                if (firstNewline > 0)
                {
                    content = content.Substring(firstNewline + 1);
                }

                var lastBackticks = content.LastIndexOf("```");
                if (lastBackticks > 0)
                {
                    content = content.Substring(0, lastBackticks);
                }
            }

            content = content.Trim();

            var jsonStart = content.IndexOf('{');
            if (jsonStart < 0)
            {
                return content;
            }

            var best = string.Empty;
            var end = content.IndexOf('}', jsonStart + 1);
            while (end > jsonStart)
            {
                var candidate = content.Substring(jsonStart, end - jsonStart + 1);
                try
                {
                    using var doc = JsonDocument.Parse(candidate, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        return candidate;
                    }
                }
                catch
                {
                    best = candidate;
                }

                end = content.IndexOf('}', end + 1);
            }

            return string.IsNullOrEmpty(best)
                ? content.Substring(jsonStart)
                : best;
        }

        private static string RepairChangesJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            json = RemoveJsonComments(json);

            json = MapChineseFieldNames(json);

            var sb = new System.Text.StringBuilder(json.Length + 64);
            var inString = false;
            var stringQuoteChar = '"';
            var escape = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c == stringQuoteChar)
                    {
                        inString = false;
                        sb.Append('"');
                        continue;
                    }

                    sb.Append(c);
                    continue;
                }

                if (c == '\'')
                {
                    inString = true;
                    stringQuoteChar = '\'';
                    sb.Append('"');
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    stringQuoteChar = '"';
                    sb.Append('"');
                    continue;
                }

                if (c == '：')
                {
                    sb.Append(':');
                    continue;
                }

                if (c == '，')
                {
                    sb.Append(',');
                    continue;
                }

                if (c == ',')
                {
                    var j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                    {
                        continue;
                    }
                    sb.Append(c);
                    continue;
                }

                if (c == '}')
                {
                    sb.Append(c);
                    var j = i + 1;
                    while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                    if (j < json.Length && json[j] == '{')
                    {
                        sb.Append(',');
                    }
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var prevNonWs = FindPrevNonWhitespace(json, i - 1);
                    if (prevNonWs == '{' || prevNonWs == ',')
                    {
                        var identEnd = i;
                        while (identEnd < json.Length && (char.IsLetterOrDigit(json[identEnd]) || json[identEnd] == '_'))
                            identEnd++;
                        var colonPos = identEnd;
                        while (colonPos < json.Length && char.IsWhiteSpace(json[colonPos]))
                            colonPos++;
                        if (colonPos < json.Length && (json[colonPos] == ':' || json[colonPos] == '：'))
                        {
                            var fieldName = json.Substring(i, identEnd - i);
                            if (ChineseFieldNameMap.TryGetValue(fieldName, out var mappedFieldName))
                                fieldName = mappedFieldName;
                            sb.Append('"').Append(fieldName).Append('"');
                            i = identEnd - 1;
                            continue;
                        }
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static char FindPrevNonWhitespace(string s, int from)
        {
            for (var i = from; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(s[i]))
                    return s[i];
            }
            return '\0';
        }

        private static string RemoveJsonComments(string json)
        {
            var sb = new System.Text.StringBuilder(json.Length);
            var inStr = false;
            var quoteChar = '"';
            var esc = false;

            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (inStr)
                {
                    sb.Append(c);
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == quoteChar) inStr = false;
                    continue;
                }

                if (c == '"' || c == '\'') { inStr = true; quoteChar = c; sb.Append(c); continue; }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    while (i < json.Length && json[i] != '\n') i++;
                    if (i < json.Length) sb.Append('\n');
                    continue;
                }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++;
                    if (i + 1 < json.Length) i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static readonly Dictionary<string, string> ChineseFieldNameMap = new(StringComparer.Ordinal)
        {
            { "角色状态变化", "CharacterStateChanges" },
            { "角色状态变更", "CharacterStateChanges" },
            { "角色变化", "CharacterStateChanges" },
            { "冲突进度", "ConflictProgress" },
            { "冲突进展", "ConflictProgress" },
            { "伏笔动作", "ForeshadowingActions" },
            { "伏笔操作", "ForeshadowingActions" },
            { "新增情节", "NewPlotPoints" },
            { "新情节点", "NewPlotPoints" },
            { "新增剧情", "NewPlotPoints" },
            { "地点状态变化", "LocationStateChanges" },
            { "地点状态变更", "LocationStateChanges" },
            { "地点变化", "LocationStateChanges" },
            { "势力状态变化", "FactionStateChanges" },
            { "势力状态变更", "FactionStateChanges" },
            { "势力变化", "FactionStateChanges" },
            { "时间推进", "TimeProgression" },
            { "时间进展", "TimeProgression" },
            { "角色移动", "CharacterMovements" },
            { "角色位移", "CharacterMovements" },
            { "物品流转", "ItemTransfers" },
            { "物品转移", "ItemTransfers" },
            { "道具流转", "ItemTransfers" },
            { "角色ID", "CharacterId" },
            { "角色编号", "CharacterId" },
            { "新等级", "NewLevel" },
            { "新能力", "NewAbilities" },
            { "失去能力", "LostAbilities" },
            { "关系变化", "RelationshipChanges" },
            { "新心理状态", "NewMentalState" },
            { "心理状态", "NewMentalState" },
            { "关键事件", "KeyEvent" },
            { "重要性", "Importance" },
            { "冲突ID", "ConflictId" },
            { "冲突编号", "ConflictId" },
            { "新状态", "NewStatus" },
            { "事件", "Event" },
            { "伏笔ID", "ForeshadowId" },
            { "伏笔编号", "ForeshadowId" },
            { "动作", "Action" },
            { "关键词", "Keywords" },
            { "上下文", "Context" },
            { "涉及角色", "InvolvedCharacters" },
            { "故事线", "Storyline" },
            { "地点ID", "LocationId" },
            { "地点编号", "LocationId" },
            { "势力ID", "FactionId" },
            { "势力编号", "FactionId" },
            { "时间段", "TimePeriod" },
            { "经过时间", "ElapsedTime" },
            { "关键时间事件", "KeyTimeEvent" },
            { "出发地", "FromLocation" },
            { "目的地", "ToLocation" },
            { "物品ID", "ItemId" },
            { "物品编号", "ItemId" },
            { "物品名称", "ItemName" },
            { "原持有者", "FromHolder" },
            { "新持有者", "ToHolder" },
        };

        private static string MapChineseFieldNames(string json)
        {
            foreach (var kv in ChineseFieldNameMap)
            {
                if (json.Contains(kv.Key))
                {
                    json = json.Replace($"\"{kv.Key}\"", $"\"{kv.Value}\"");
                    json = json.Replace($"'{kv.Key}'", $"\"{kv.Value}\"");
                }
            }
            return json;
        }

        private void ValidateRequiredFields(ProtocolValidationResult result, string jsonStr)
        {
            if (result.Changes == null)
            {
                result.AddError("CHANGES对象为空");
                return;
            }

            var requiredFields = new[] 
            { 
                "CharacterStateChanges", 
                "ConflictProgress", 
                "ForeshadowingActions", 
                "NewPlotPoints",
                "LocationStateChanges",
                "FactionStateChanges",
                "TimeProgression",
                "CharacterMovements",
                "ItemTransfers"
            };

            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    result.AddError("CHANGES JSON 不是对象");
                    return;
                }

                foreach (var field in requiredFields)
                {
                    if (!HasPropertyIgnoreCase(doc.RootElement, field))
                    {
                        result.AddError($"CHANGES缺失必需字段: {field}（必须显式声明，即使为空数组）");
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(ValidateRequiredFields), ex);
                foreach (var field in requiredFields)
                {
                    if (!jsonStr.Contains($"\"{field}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddError($"CHANGES缺失必需字段: {field}（必须显式声明，即使为空数组）");
                    }
                }
            }
        }

        private static bool HasPropertyIgnoreCase(JsonElement obj, string propertyName)
        {
            if (obj.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void ValidateShortIdFields(ProtocolValidationResult result)
        {
            var changes = result.Changes;
            if (changes == null) return;

            void Require(string field, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && !ShortIdGenerator.IsLikelyId(value))
                    result.AddError($"CHANGES协议违规：{field} 必须为 ShortId（格式：13字符大写字母开头，如`D7M3VT2K9P4N`），收到非法值'{value}'");
            }

            foreach (var ch in changes.CharacterStateChanges ?? new())
            {
                Require("CharacterStateChanges[].CharacterId", ch.CharacterId);
                foreach (var key in ch.RelationshipChanges?.Keys ?? Enumerable.Empty<string>())
                    Require("CharacterStateChanges[].RelationshipChanges.key", key);
            }

            foreach (var cp in changes.ConflictProgress ?? new())
                Require("ConflictProgress[].ConflictId", cp.ConflictId);

            foreach (var fa in changes.ForeshadowingActions ?? new())
                Require("ForeshadowingActions[].ForeshadowId", fa.ForeshadowId);

            foreach (var loc in changes.LocationStateChanges ?? new())
                Require("LocationStateChanges[].LocationId", loc.LocationId);

            foreach (var fac in changes.FactionStateChanges ?? new())
                Require("FactionStateChanges[].FactionId", fac.FactionId);

            foreach (var move in changes.CharacterMovements ?? new())
            {
                Require("CharacterMovements[].CharacterId", move.CharacterId);
                if (!string.IsNullOrWhiteSpace(move.FromLocation))
                    Require("CharacterMovements[].FromLocation", move.FromLocation);
                if (!string.IsNullOrWhiteSpace(move.ToLocation))
                    Require("CharacterMovements[].ToLocation", move.ToLocation);
            }

            foreach (var transfer in changes.ItemTransfers ?? new())
            {
                Require("ItemTransfers[].ItemId", transfer.ItemId);
                if (!string.IsNullOrWhiteSpace(transfer.FromHolder))
                    Require("ItemTransfers[].FromHolder", transfer.FromHolder);
                if (!string.IsNullOrWhiteSpace(transfer.ToHolder))
                    Require("ItemTransfers[].ToHolder", transfer.ToHolder);
            }

            foreach (var pp in changes.NewPlotPoints ?? new())
                foreach (var ic in pp.InvolvedCharacters ?? new())
                    Require("NewPlotPoints[].InvolvedCharacters[]", ic);
        }

        private List<string> ValidateWorldRuleConstraints(string content, List<WorldRuleConstraint> constraints)
        {
            var violations = new List<string>();
            if (string.IsNullOrEmpty(content))
                return violations;

            foreach (var rule in constraints.Where(c => c.IsHardConstraint))
            {
                if (string.IsNullOrEmpty(rule.Constraint))
                    continue;

                var violation = CheckConstraintViolation(content, rule);
                if (violation != null)
                {
                    violations.Add($"世界观硬约束违反 [{rule.RuleName}]: {violation}");
                }
            }

            return violations;
        }

        private string? CheckConstraintViolation(string content, WorldRuleConstraint rule)
        {
            var constraint = rule.Constraint;

            var negationPatterns = new[] { "不能", "不可", "禁止", "无法", "不得", "不会" };
            foreach (var negation in negationPatterns)
            {
                var idx = constraint.IndexOf(negation, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var actionStart = idx + negation.Length;
                    var actionLength = Math.Min(10, constraint.Length - actionStart);
                    if (actionLength > 0)
                    {
                        var forbiddenAction = constraint.Substring(actionStart, actionLength).Trim();
                        forbiddenAction = forbiddenAction.TrimEnd('，', '。', '、', '；');

                        if (!string.IsNullOrEmpty(forbiddenAction) && forbiddenAction.Length >= 2)
                        {
                            if (HasViolatingOccurrence(content, forbiddenAction))
                            {
                                return $"正文出现被禁止的内容「{forbiddenAction}」（约束：{constraint}）";
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool HasViolatingOccurrence(string content, string forbiddenAction)
        {
            var negationPrefixes = new[] { "不能", "不可", "禁止", "无法", "不得", "不会", "别", "莫", "勿", "未曾", "从未", "并未", "没有", "岂能", "怎能", "哪能", "焉能" };

            var searchStart = 0;
            while (searchStart < content.Length)
            {
                var pos = content.IndexOf(forbiddenAction, searchStart, StringComparison.Ordinal);
                if (pos < 0) break;

                var contextStart = Math.Max(0, pos - 15);
                var prefix = content.Substring(contextStart, pos - contextStart);
                var sentBreak = prefix.LastIndexOfAny(new[] { '。', '！', '？', '；', '\n' });
                if (sentBreak >= 0) prefix = prefix.Substring(sentBreak + 1);

                var isNegated = false;
                foreach (var neg in negationPrefixes)
                {
                    if (prefix.Contains(neg, StringComparison.Ordinal))
                    {
                        isNegated = true;
                        break;
                    }
                }

                if (!isNegated)
                {
                    return true;
                }

                searchStart = pos + forbiddenAction.Length;
            }

            return false;
        }

        #endregion
    }
}
