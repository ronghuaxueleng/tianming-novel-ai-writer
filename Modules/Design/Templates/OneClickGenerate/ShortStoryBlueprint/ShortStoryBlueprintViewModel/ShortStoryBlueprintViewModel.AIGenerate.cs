using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Metadata;
using TM.Services.Framework.AI.Interfaces.Prompts;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint
{
    public partial class ShortStoryBlueprintViewModel
    {
        private static readonly Regex ChapterSplitRegex =
            new(@"\[CH\d+\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChapterMarkerRegex =
            new(@"\[CH(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        protected override IEnumerable<string> GetExistingNamesForDedup()
            => Service.GetAllBlueprints().Select(r => r.Name);

        protected override bool SupportsBatch(TM.Framework.Common.Controls.TreeNodeItem categoryNode) => false;

        protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            UpdateAIGenerateEnabledState();
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (!base.CanExecuteAIGenerate()) return false;
            return int.TryParse(FormTotalChapters?.Trim(), out var n) && n > 0
                   && !string.IsNullOrWhiteSpace(FormBookAnalysisId);
        }

        private AIGenerationConfig? _cachedConfig;
        protected override AIGenerationConfig? GetAIGenerationConfig()
        {
            return _cachedConfig ??= new AIGenerationConfig
            {
                Category = "短篇蓝图设计师",
                ServiceType = AIServiceType.ChatEngine,
                ResponseFormat = ResponseFormat.Json,
                MessagePrefix = "生成短篇蓝图",
                ProgressMessage = "正在生成章节蓝图，请稍候...",
                CompleteMessage = "蓝图已生成，请检查并编辑",
                InputVariables = new()
                {
                    ["短篇名称"] = () => _currentEditingCategory != null && _currentEditingData == null ? string.Empty : FormName,
                    ["来源拆书"] = () => FormSourceBookName,
                    ["题材类型"] = () => FormGenre,
                    ["总章节数"] = () => FormTotalChapters,
                    ["每章字数"] = () => FormWordsPerChapter,
                    ["基调氛围"] = () => FormToneGuide,
                    ["全篇简介"] = () => FormSynopsis,
                },
                OutputFields = new()
                {
                    ["全篇简介"] = v => FormSynopsis = v,
                    ["章节蓝图"] = v => ApplyBlueprintText(v),
                },
                OutputFieldGetters = new()
                {
                    ["全篇简介"] = () => FormSynopsis,
                    ["章节蓝图"] = () => ExportBlueprintText(),
                },
                BatchFieldKeyMap = EntityFieldMeta.GetFieldKeyMapWithName("shortstoryblueprint"),
                BatchIndexFields = new() { "Name", "Synopsis" }
            };
        }

        protected override List<Dictionary<string, object>> ParseBatchJsonResult(string jsonResponse)
        {
            var result = base.ParseBatchJsonResult(jsonResponse);
            if (result.Count > 0)
                return result;

            try
            {
                var jsonObject = ExtractJsonObjectFromResponse(jsonResponse);
                using var document = System.Text.Json.JsonDocument.Parse(jsonObject);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return new List<Dictionary<string, object>>();

                var entity = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    object value = property.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        System.Text.Json.JsonValueKind.Number => property.Value.TryGetInt32(out var intVal)
                            ? intVal
                            : (object)property.Value.GetDouble(),
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        _ => property.Value.ToString() ?? string.Empty
                    };
                    entity[property.Name] = value;
                }

                var config = GetAIGenerationConfig();
                if (config?.BatchFieldKeyMap != null && config.BatchFieldKeyMap.Count > 0)
                {
                    foreach (var kv in config.BatchFieldKeyMap)
                    {
                        var sourceKey = kv.Key;
                        var targetKey = kv.Value;
                        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey))
                            continue;
                        if (entity.ContainsKey(targetKey))
                            continue;
                        if (entity.TryGetValue(sourceKey, out var v) && v != null)
                            entity[targetKey] = v;
                    }
                }

                return new List<Dictionary<string, object>> { entity };
            }
            catch
            {
                return new List<Dictionary<string, object>>();
            }
        }

        protected override async Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            var result = new List<Dictionary<string, object>>();
            var dbNames = new HashSet<string>(Service.GetAllBlueprints().Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var targetCount = int.TryParse(FormTotalChapters?.Trim(), out var n) && n > 0 ? n : 0;

            Service.BeginBatchSave();
            try
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                        var name = reader.GetString("Name");
                        if (string.IsNullOrWhiteSpace(name))
                            name = $"蓝图_{DateTime.Now:HHmmss}_{result.Count + 1}";

                        var baseName = name;
                        if (dbNames.Contains(baseName))
                            continue;

                        int suffix = 1;
                        while (batchNames.Contains(name))
                            name = $"{baseName}_{suffix++}";

                        batchNames.Add(name);
                        dbNames.Add(name);

                        var synopsis = reader.GetString("Synopsis");
                        if (string.IsNullOrWhiteSpace(synopsis))
                            synopsis = reader.GetString("全篇简介") ?? string.Empty;

                        var blueprintText = reader.GetString("ChapterBlueprintText");
                        if (string.IsNullOrWhiteSpace(blueprintText))
                            blueprintText = reader.GetString("章节蓝图");

                        var chapterBlueprints = ParseBlueprintTextToData(blueprintText, targetCount);

                        var data = new ShortStoryBlueprintData
                        {
                            Id = ShortIdGenerator.New("D"),
                            Name = name,
                            Category = categoryName,
                            Icon = DefaultDataIcon,
                            IsEnabled = true,
                            CreatedTime = DateTime.Now,
                            ModifiedTime = DateTime.Now,
                            SourceBookName = FormSourceBookName,
                            Genre = FormGenre,
                            TotalChapters = targetCount > 0 ? targetCount.ToString() : (FormTotalChapters ?? string.Empty),
                            WordsPerChapter = FormWordsPerChapter,
                            ToneGuide = FormToneGuide,
                            Synopsis = synopsis,
                            ChapterBlueprints = chapterBlueprints,
                            DependencyModuleVersions = versionSnapshot ?? new()
                        };

                        entity["Name"] = name;
                        await Service.AddBlueprintAsync(data);
                        result.Add(entity);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ShortStoryBlueprintViewModel] SaveBatchEntitiesAsync: 保存实体失败 - {ex.Message}");
                    }
                }

                if (result.Count > 0)
                {
                    var genreToSync = FormGenre;
                    if (string.IsNullOrWhiteSpace(genreToSync))
                    {
                        var categoryNames = CollectCategoryAndChildrenNames(categoryName);
                        genreToSync = Service.GetAllBlueprints()
                            .Where(m => categoryNames.Contains(m.Category) && m.IsEnabled && !string.IsNullOrWhiteSpace(m.Genre))
                            .OrderByDescending(m => m.ModifiedTime)
                            .FirstOrDefault()?.Genre ?? string.Empty;
                    }
                    if (!string.IsNullOrWhiteSpace(genreToSync))
                        await SyncSpecWithGenreAsync(genreToSync);
                }

                return result;
            }
            finally
            {
                Service.EndBatchSave();
            }
        }

        private List<ShortStoryChapterBlueprint> ParseBlueprintTextToData(string blueprintText, int targetCount)
        {
            var items = ParseBlueprintTextToVMs(blueprintText);
            if (items.Count == 0)
                return new List<ShortStoryChapterBlueprint>();

            if (targetCount > 0)
                items = NormalizeChapters(items, targetCount);

            return items.OrderBy(x => x.ChapterIndex).Select(vm => new ShortStoryChapterBlueprint
            {
                ChapterIndex = vm.ChapterIndex,
                Title = vm.Title,
                KeyEvents = vm.KeyEvents,
                Characters = vm.Characters,
                EndingNote = vm.EndingNote,
                TargetWordCount = vm.TargetWordCount
            }).ToList();
        }

        private static List<ShortStoryChapterBlueprintVM> ParseBlueprintTextToVMs(string blueprintText)
        {
            if (string.IsNullOrWhiteSpace(blueprintText))
                return new List<ShortStoryChapterBlueprintVM>();

            var newItems = new List<ShortStoryChapterBlueprintVM>();
            var chapterBlocks = ChapterSplitRegex.Split(blueprintText);

            var markers = ChapterMarkerRegex.Matches(blueprintText);

            for (int i = 0; i < markers.Count; i++)
            {
                var blockText = i + 1 < chapterBlocks.Length ? chapterBlocks[i + 1] : string.Empty;
                var chapterIndex = int.TryParse(markers[i].Groups[1].Value, out var ci) ? ci : i + 1;

                var vm = new ShortStoryChapterBlueprintVM { ChapterIndex = chapterIndex };
                string? lastKey = null;
                foreach (var line in blockText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx <= 0)
                    {
                        var val2 = line.Trim();
                        if (string.IsNullOrWhiteSpace(val2) || string.IsNullOrWhiteSpace(lastKey))
                            continue;

                        switch (lastKey)
                        {
                            case "keyevents":
                                vm.KeyEvents = string.IsNullOrWhiteSpace(vm.KeyEvents) ? val2 : vm.KeyEvents + "\n" + val2;
                                break;
                            case "characters":
                                vm.Characters = string.IsNullOrWhiteSpace(vm.Characters) ? val2 : vm.Characters + "\n" + val2;
                                break;
                            case "endingnote":
                                vm.EndingNote = string.IsNullOrWhiteSpace(vm.EndingNote) ? val2 : vm.EndingNote + "\n" + val2;
                                break;
                            case "title":
                                vm.Title = string.IsNullOrWhiteSpace(vm.Title) ? val2 : vm.Title + "\n" + val2;
                                break;
                            case "targetwordcount":
                                vm.TargetWordCount = string.IsNullOrWhiteSpace(vm.TargetWordCount) ? val2 : vm.TargetWordCount + "\n" + val2;
                                break;
                        }

                        continue;
                    }

                    var key = line[..colonIdx].Trim().ToLowerInvariant();
                    var val = line[(colonIdx + 1)..].Trim();
                    switch (key)
                    {
                        case "title":
                        case "标题":
                        case "章节标题":
                            vm.Title = val;
                            lastKey = "title";
                            break;
                        case "keyevents":
                        case "key_events":
                        case "主要事件":
                        case "关键事件":
                            vm.KeyEvents = val;
                            lastKey = "keyevents";
                            break;
                        case "characters":
                        case "出场人物":
                        case "人物":
                            vm.Characters = val;
                            lastKey = "characters";
                            break;
                        case "endingnote":
                        case "ending_note":
                        case "结尾方向":
                        case "结尾":
                            vm.EndingNote = val;
                            lastKey = "endingnote";
                            break;
                        case "targetwordcount":
                        case "target_word_count":
                        case "字数":
                        case "本章字数":
                            vm.TargetWordCount = val;
                            lastKey = "targetwordcount";
                            break;
                    }
                }
                if (string.IsNullOrWhiteSpace(vm.Title))
                    vm.Title = $"第{chapterIndex}章";
                newItems.Add(vm);
            }

            return newItems;
        }

        private static List<ShortStoryChapterBlueprintVM> NormalizeChapters(List<ShortStoryChapterBlueprintVM> items, int targetCount)
        {
            items = items.OrderBy(x => x.ChapterIndex).ToList();
            if (targetCount <= 0)
                return items;

            if (items.Count > targetCount)
                items = items.Take(targetCount).ToList();

            while (items.Count < targetCount)
            {
                var idx = items.Count + 1;
                items.Add(new ShortStoryChapterBlueprintVM
                {
                    ChapterIndex = idx,
                    Title = $"第{idx}章"
                });
            }

            for (int i = 0; i < items.Count; i++)
                items[i].ChapterIndex = i + 1;

            return items;
        }

        private void ApplyBlueprintText(string blueprintText)
        {
            if (string.IsNullOrWhiteSpace(blueprintText)) return;

            var newItems = ParseBlueprintTextToVMs(blueprintText);
            if (newItems.Count == 0) return;

            var targetCount = int.TryParse(FormTotalChapters?.Trim(), out var n) && n > 0 ? n : 0;
            if (targetCount > 0)
            {
                var before = newItems.Count;
                newItems = NormalizeChapters(newItems, targetCount);
                if (before != targetCount)
                    GlobalToast.Warning("章节数不匹配", $"AI返回 {before} 章，已按总章节数 {targetCount} 章裁剪/补齐");
            }

            _ = Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                ChapterBlueprints.ReplaceAll(newItems.OrderBy(x => x.ChapterIndex).ToList());

                _currentChapterIndex = 0;
                OnPropertyChanged(nameof(CurrentChapterBlueprint));
                OnPropertyChanged(nameof(CurrentChapterLabel));
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            });

            _ = SyncSpecWithGenreAsync(FormGenre);

            TM.App.Log($"[ShortStoryBlueprintViewModel] 章节蓝图已解析回填: {newItems.Count} 章");
        }

        private async Task SyncSpecWithGenreAsync(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre) || _promptRepository == null) return;
            try
            {
                var specTemplates = _promptRepository.GetTemplatesByCategory(genre);
                var specTemplate = specTemplates?
                    .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                    .OrderByDescending(t => t.IsDefault)
                    .ThenByDescending(t => t.IsBuiltIn)
                    .FirstOrDefault();
                if (specTemplate == null) return;

                var spec = SpecTemplateParser.Parse(specTemplate.SystemPrompt, specTemplate.Name);
                await _specLoader.SaveProjectSpecAsync(spec);
                _specLoader.InvalidateCache();
                TM.App.Log($"[ShortStoryBlueprintViewModel] Spec 已同步为题材: {genre} → {specTemplate.Name}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ShortStoryBlueprintViewModel] Spec 同步失败: {ex.Message}");
            }
        }

        private string ExportBlueprintText()
        {
            if (ChapterBlueprints.Count == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in ChapterBlueprints)
            {
                sb.AppendLine($"[CH{ch.ChapterIndex}]");
                sb.AppendLine($"title: {ch.Title}");
                sb.AppendLine($"keyEvents: {ch.KeyEvents}");
                sb.AppendLine($"characters: {ch.Characters}");
                sb.AppendLine($"endingNote: {ch.EndingNote}");
                sb.AppendLine($"targetWordCount: {ch.TargetWordCount}");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
