using System;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 短篇蓝图会话

        private static string? TryParseImitateDirective(string userText)
        {
            foreach (var keyword in new[] { "@仿写:", "@仿写：", "@imitate:" })
            {
                var idx = userText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var start = idx + keyword.Length;
                var end = userText.IndexOfAny(new[] { ' ', '\n', '\r', '\t' }, start);
                var name = end > start ? userText[start..end] : userText[start..];
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }
            return null;
        }

        private async Task StartBlueprintSessionAsync(string blueprintId, string blueprintName)
        {
            try
            {
                var blueprintSvc = ServiceLocator.Get<TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.Services.ShortStoryBlueprintService>();
                var blueprint = blueprintSvc.GetBlueprintById(blueprintId);
                if (blueprint == null)
                {
                    GlobalToast.Error("蓝图不存在", $"未找到蓝图：{blueprintName}");
                    return;
                }

                if (!int.TryParse(blueprint.TotalChapters, out var totalChapters) || totalChapters <= 0)
                {
                    GlobalToast.Warning("蓝图配置不完整", "请先设置总章节数再开始生成");
                    return;
                }

                if (blueprint.ChapterBlueprints.Count == 0)
                {
                    GlobalToast.Warning("蓝图为空", "请先填写或生成章节蓝图内容");
                    return;
                }

                _blueprintSessionId = blueprintId;
                _blueprintSessionName = blueprintName;
                _blueprintTotalChapters = totalChapters;
                _blueprintSavedCount = 0;
                _blueprintNextChapterIndex = 1;
                _blueprintCts?.Dispose();
                _blueprintCts = new CancellationTokenSource();

                TM.App.Log($"[SKConversationViewModel] 蓝图会话开始: {blueprintName} 共{totalChapters}章");

                await GenerateBlueprintChapterAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] StartBlueprintSessionAsync 失败: {ex.Message}");
                GlobalToast.Error("启动蓝图会话失败", $"启动失败：{ex.Message}");
            }
        }

        private async Task GenerateBlueprintChapterAsync()
        {
            if (string.IsNullOrEmpty(_blueprintSessionId)) return;

            if (_blueprintNextChapterIndex > _blueprintTotalChapters)
            {
                ClearBlueprintSession();
                GlobalToast.Success("生成完毕", $"「{_blueprintSessionName}」全篇 {_blueprintTotalChapters} 章已全部生成");
                return;
            }

            try
            {
                IsGenerating = true;
                var chapterIndex = _blueprintNextChapterIndex;
                TM.App.Log($"[SKConversationViewModel] 蓝图生成第{chapterIndex}章...");

                var writer = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.Plugins.WriterPlugin>();
                var result = await writer.GenerateChapterFromBlueprintAsync(
                    _blueprintCts?.Token ?? CancellationToken.None,
                    _blueprintSessionId!,
                    chapterIndex);

                var (isSavedCancelled, _) = UIMessageItem.TryExtractCancelledPartial(result.SavedContent);
                if (!result.SavedContent.StartsWith("[错误]", StringComparison.Ordinal) && !isSavedCancelled)
                {
                    _blueprintSavedCount++;
                    _blueprintNextChapterIndex++;

                    var progressText = $"（{_blueprintSavedCount}/{_blueprintTotalChapters}）";
                    var chapterLabel = !string.IsNullOrEmpty(result.Title) ? result.Title : $"第{chapterIndex}章";
                    Messages.Add(UIMessageItem.CreateSystemMessage(
                        $"✅ 已生成：{chapterLabel} {progressText}\n\n{result.DisplayContent[..Math.Min(200, result.DisplayContent.Length)]}..."));

                    _blueprintProgressLabel = progressText;
                    _blueprintNextLabel = _blueprintNextChapterIndex <= _blueprintTotalChapters
                        ? $"▶ 继续第{_blueprintNextChapterIndex}章 {progressText}"
                        : string.Empty;
                    _hasBlueprintSessionActions = _blueprintNextChapterIndex <= _blueprintTotalChapters;

                    OnPropertyChanged(nameof(ImitationProgressLabel));
                    OnPropertyChanged(nameof(BlueprintNextLabel));
                    OnPropertyChanged(nameof(HasBlueprintSessionActions));
                    OnPropertyChanged(nameof(HasSuggestedActions));

                    if (_blueprintNextChapterIndex > _blueprintTotalChapters)
                    {
                        await Task.Delay(300);
                        ClearBlueprintSession();
                    }
                }
                else
                {
                    GlobalToast.Error("章节生成失败", result.SavedContent);
                    _hasBlueprintSessionActions = !string.IsNullOrEmpty(_blueprintSessionId);
                    _blueprintNextLabel = $"↺ 重试第{chapterIndex}章";
                    OnPropertyChanged(nameof(BlueprintNextLabel));
                    OnPropertyChanged(nameof(HasBlueprintSessionActions));
                    OnPropertyChanged(nameof(HasSuggestedActions));
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] GenerateBlueprintChapterAsync 失败: {ex.Message}");
                GlobalToast.Error("章节生成失败", $"章节生成失败：{ex.Message}");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void ClearBlueprintSession()
        {
            _blueprintSessionId = null;
            _blueprintSessionName = string.Empty;
            _blueprintTotalChapters = 0;
            _blueprintSavedCount = 0;
            _blueprintNextChapterIndex = 1;
            _hasBlueprintSessionActions = false;
            _blueprintProgressLabel = string.Empty;
            _blueprintNextLabel = string.Empty;
            try
            {
                _blueprintCts?.Cancel();
                _blueprintCts?.Dispose();
            }
            catch (Exception ex) { TM.App.Log($"[SKConversationViewModel] ClearImitationSession 取消蓝图CTS异常: {ex.Message}"); }
            _blueprintCts = null;
            OnPropertyChanged(nameof(ImitationProgressLabel));
            OnPropertyChanged(nameof(BlueprintNextLabel));
            OnPropertyChanged(nameof(HasBlueprintSessionActions));
            OnPropertyChanged(nameof(HasSuggestedActions));
        }

        #endregion
    }
}
