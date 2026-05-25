using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Config;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        private static string FormatErrorMessage(Exception ex)
        {
            if (ex is OperationCanceledException) return "操作已取消";
            if (ex is TimeoutException) return "请求超时";
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("The operation was canceled", StringComparison.OrdinalIgnoreCase)) return "操作已取消";
            if (msg.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase)) return "操作已取消";
            if (msg.Contains("canceled", StringComparison.OrdinalIgnoreCase)) return "操作已取消";
            if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return "请求超时";
            return msg;
        }

        private const string IdentityLocalAnswer = "我是「天命」——由「子夜」开发的智能创作助手。";

        private async Task SendDeveloperLevelResponseAsync(string userText)
        {
            IsGenerating = true;
            var startTime = DateTime.Now;
            UIMessageItem? assistantMessage = null;

            try
            {
                var userMessage = UIMessageItem.CreateUserMessage(userText);
                Messages.Add(userMessage);

                assistantMessage = UIMessageItem.CreateAssistantPlaceholder();
                assistantMessage.AnalysisSummary = "Thinking...";
                assistantMessage.IsThinking = true;
                Messages.Add(assistantMessage);

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        var runId = ExecutionEventHub.NewRunId();
                        _chatService.SetLastRunId(runId);
                        userMessage.RunId = runId;
                        assistantMessage.RunId = runId;

                        assistantMessage.Thinking.WriteCompletion("当前模型无思考模式");

                        assistantMessage.AppendContent(IdentityLocalAnswer);

                        assistantMessage.IsThinking = false;
                        assistantMessage.Thinking.Complete();
                        assistantMessage.FinishStreaming();
                    });
                }

                _chatService.SaveMessages(Messages);
                SyncSessionFromServiceAfterPersist();
                TM.App.Log($"[SKConversationViewModel] 开发级问题本地硬答完成: {IdentityLocalAnswer.Length} 字符");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 开发级问题响应失败: {ex.Message}");

                var friendly = FormatErrorMessage(ex);
                if (assistantMessage != null)
                {
                    assistantMessage.IsThinking = false;
                    assistantMessage.IsError = true;
                    assistantMessage.Content = $"响应失败：{friendly}";
                    assistantMessage.FinishStreaming();
                }
                else
                {
                    Messages.Add(UIMessageItem.CreateErrorMessage($"响应失败：{friendly}"));
                }
            }
            finally
            {
                IsGenerating = false;
                RefreshContextUsage();

                FinalizeAssistantMessageIfIncomplete(assistantMessage, startTime);
            }
        }

        private void CancelGeneration()
        {
            if (CurrentMode == ChatMode.Agent && _currentExecutionAssistantMessage != null && _todoExecutionService.IsRunning)
            {
                _wasExecutionCancelledByUser = true;

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _currentExecutionAssistantMessage!.IsThinking = false;
                    _currentExecutionAssistantMessage.AnalysisSummary = "已取消";
                    _currentExecutionAssistantMessage.Content = SanitizeFinalBubbleContent("创作任务已取消。");
                    _currentExecutionAssistantMessage.IsError = true;
                    _currentExecutionAssistantMessage.FinishStreaming();
                });
            }

            _chatService.CancelCurrentRequest();

            try { _prebuiltSimulationCts?.Cancel(); } catch (Exception ex) { TM.App.Log($"[SKConversationViewModel] CancelGeneration 取消预构建流式输出异常: {ex.Message}"); }

            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
            }

            IsGenerating = false;
            TM.App.Log("[SKConversationViewModel] 已取消生成");
        }

        private async Task<ConversationMessage> ParseAndPublishPlanStepsAsync(string userInput, string rawContent, string? thinking, Guid runId)
        {
            try
            {
                var normalizedInput = NormalizeChapterHint(userInput, rawContent);
                var hasContinue = ChapterDirectiveParser.HasContinueDirective(userInput);
                var hasRewrite = ChapterDirectiveParser.HasRewriteDirective(userInput);
                var inputForPlan = hasContinue || hasRewrite ? userInput : normalizedInput;

                _pendingContinueSourceId = null;
                _pendingRewriteTargetId = null;
                if (hasContinue)
                {
                    var rawSourceToken = ChapterDirectiveParser.ParseSourceChapterId(userInput);
                    if (!string.IsNullOrEmpty(rawSourceToken))
                    {
                        _pendingContinueSourceId = await ResolveChapterIdTokenAsync(rawSourceToken);
                        TM.App.Log($"[SKConversationViewModel] Plan缓存续写来源: {rawSourceToken} → {_pendingContinueSourceId}");
                    }
                }
                else if (hasRewrite)
                {
                    var rawTargetToken = ChapterDirectiveParser.ParseTargetChapterId(userInput);
                    if (!string.IsNullOrEmpty(rawTargetToken))
                    {
                        _pendingRewriteTargetId = await ResolveChapterIdTokenAsync(rawTargetToken);
                        TM.App.Log($"[SKConversationViewModel] Plan缓存重写目标: {rawTargetToken} → {_pendingRewriteTargetId}");
                    }
                }

                var profile = ModeProfileRegistry.GetProfile(ChatMode.Plan);
                var message = await Task.Run(async () => await profile.Mapper.MapFromStreamingResultAsync(inputForPlan, rawContent, thinking).ConfigureAwait(false));

                var planPayload = message.Payload as PlanPayload;
                if (planPayload != null && planPayload.Steps.Count > 0)
                {
                    _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload, runId);

                    _comm.PublishShowPlanViewChanged(true);
                }
                else
                {
                    _cachedPlanSteps = null;
                }

                if (_cachedPlanSteps == null)
                {
                    TM.App.Log("[SKConversationViewModel] 未解析到计划步骤");
                }

                return message;
            }
            catch (Exception ex)
            {
                _cachedPlanSteps = null;
                TM.App.Log($"[SKConversationViewModel] 解析计划步骤失败: {ex.Message}");

                return new ConversationMessage
                {
                    RunId = runId,
                    Summary = "[!] 计划解析失败，请重新描述您的需求。"
                };
            }
        }

        private static bool ShouldUseTools(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText))
                return false;

            var t = userText.Trim();

            if (t.Contains('@'))
                return true;

            if (ContainsBusinessReference(t))
                return true;

            if (ContainsActionIntent(t))
                return true;

            if (IsCasualChat(t))
                return false;

            return true;
        }

        private static readonly string[] DesignTerms =
        {
            "角色", "主角", "配角", "人物", "反派", "龙套",
            "世界观", "世界规则", "体系", "设定", "修炼",
            "势力", "门派", "阵营", "组织", "家族",
            "地点", "场景", "地图", "位置",
            "情节", "剧情", "冲突", "伏笔", "线索", "悬念", "转折",
            "金手指", "能力", "技能", "法术",
            "规则", "约束", "硬约束"
        };

        private static readonly string[] CreativeTerms =
        {
            "素材", "模板", "创作", "灵感", "构思",
            "文风", "风格", "流派", "题材",
            "拆书", "分析结果",
            "塑造", "人物关系", "关系图"
        };

        private static readonly string[] ContentTerms =
        {
            "章", "章节", "卷", "分卷", "大纲", "纲要",
            "正文", "内容", "字数", "篇幅",
            "蓝图", "导引", "摘要",
            "续写", "重写", "改写",
            "已生成", "未生成", "生成进度"
        };

        private static readonly string[] ProjectTerms =
        {
            "项目", "作品", "小说", "书", "故事",
            "数据", "知识库", "索引",
            "打包", "发布", "校验", "验证"
        };

        private static bool ContainsBusinessReference(string text)
        {
            foreach (var term in DesignTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var term in CreativeTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var term in ContentTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var term in ProjectTerms)
                if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool ContainsActionIntent(string text)
        {
            return text.Contains("修改") || text.Contains("编辑") || text.Contains("删除") || text.Contains("添加")
                || text.Contains("创建") || text.Contains("写入") || text.Contains("更新")
                || text.Contains("替换") || text.Contains("插入") || text.Contains("移动") || text.Contains("重命名")
                || text.Contains("查找") || text.Contains("搜索") || text.Contains("查询") || text.Contains("查看")
                || text.Contains("优化") || text.Contains("重构") || text.Contains("调整")
                || text.Contains("设置") || text.Contains("配置") || text.Contains("保存") || text.Contains("导出")
                || text.Contains("帮我") || text.Contains("请你") || text.Contains("麻烦");
        }

        private static bool IsCasualChat(string text)
        {
            if (text.Length <= 4)
                return true;

            if (text.StartsWith("你好") || text.StartsWith("hi", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("hello", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("嗨") || text.StartsWith("哈喽") || text.StartsWith("在吗")
                || text.StartsWith("谢谢") || text.StartsWith("感谢") || text.StartsWith("好的")
                || text.StartsWith("了解") || text.StartsWith("明白") || text.StartsWith("收到"))
            {
                return text.Length <= 10;
            }

            if ((text.StartsWith("什么是") || text.StartsWith("为什么") || text.StartsWith("怎样理解")
                || text.StartsWith("解释") || text.StartsWith("介绍") || text.StartsWith("你觉得")
                || text.StartsWith("你认为"))
                && text.Length <= 30)
            {
                return true;
            }

            return false;
        }

        private static bool IsBusinessFactQuery(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return false;
            var t = userText.Trim();

            if (!ContainsBusinessReference(t)) return false;

            if (t.Contains("修改") || t.Contains("编辑") || t.Contains("删除") || t.Contains("添加")
                || t.Contains("创建") || t.Contains("写入") || t.Contains("更新")
                || t.Contains("替换") || t.Contains("插入") || t.Contains("移动") || t.Contains("重命名"))
                return false;

            if (ContainsQueryIntent(t)) return true;

            if (t.EndsWith("?", StringComparison.Ordinal) || t.EndsWith("？")) return true;

            return false;
        }

        private static bool ContainsQueryIntent(string text)
        {
            return text.Contains("是谁") || text.Contains("是什么") || text.Contains("有哪些")
                || text.Contains("有几") || text.Contains("多少") || text.Contains("哪个")
                || text.Contains("哪些") || text.Contains("列出") || text.Contains("告诉我")
                || text.Contains("当前") || text.Contains("详情") || text.Contains("叫什么")
                || text.Contains("是否") || text.Contains("存在") || text.Contains("有没有")
                || text.Contains("什么样") || text.Contains("怎么样")
                || text.Contains("查找") || text.Contains("搜索") || text.Contains("查询") || text.Contains("查看");
        }

        private static string[]? GetForcedFunctionNames(string userText)
        {
            var t = userText.Trim();

            if (t.Contains("素材") || t.Contains("模板") || t.Contains("创作")
                || t.Contains("文风") || t.Contains("风格") || t.Contains("流派") || t.Contains("题材")
                || t.Contains("灵感") || t.Contains("构思")
                || t.Contains("拆书") || t.Contains("分析结果")
                || t.Contains("塑造") || t.Contains("人物关系") || t.Contains("关系图"))
                return new[] { "SearchCreativeMaterials", "GetCreativeMaterialById", "GetCreativeMaterialsByIds" };

            if (t.Contains("大纲") || t.Contains("纲要"))
                return new[] { "SearchOutlines", "GetOutlineById", "GetOutlinesByIds" };

            if (t.Contains("分卷") || t.Contains("卷"))
                return new[] { "SearchVolumeDesigns", "GetVolumeDesignById", "GetVolumeDesignsByIds" };

            if (t.Contains("蓝图"))
                return new[] { "SearchBlueprints", "GetBlueprintById", "GetBlueprintsByIds" };

            if (t.Contains("正文") || t.Contains("字数") || t.Contains("篇幅")
                || t.Contains("内容") || t.Contains("导引") || t.Contains("摘要")
                || t.Contains("已生成") || t.Contains("未生成") || t.Contains("生成进度"))
                return new[] { "SearchContent", "FindRelatedChapters",
                               "ListGeneratedChapters", "ListUngeneratedChapters", "GetGeneratedChaptersSummary" };

            if (t.Contains("续写") || t.Contains("重写") || t.Contains("改写"))
                return new[] { "SearchChapterPlans", "GetChapterPlanById", "GetChapterPlansByIds",
                               "ListGeneratedChapters", "GetGeneratedChaptersSummary",
                               "GetExpandedChapterContext", "GetChapterContext" };

            if (t.Contains("章节") || t.Contains("章"))
                return new[] { "SearchChapterPlans", "GetChapterPlanById", "GetChapterPlansByIds",
                               "ListGeneratedChapters", "ListUngeneratedChapters", "GetGeneratedChaptersSummary",
                               "GetExpandedChapterContext", "GetChapterContext" };

            if (t.Contains("角色") || t.Contains("主角") || t.Contains("配角")
                || t.Contains("人物") || t.Contains("反派") || t.Contains("龙套"))
                return new[] { "SearchCharacters", "GetCharacterById", "GetCharactersByIds", "GetProtagonists" };

            if (t.Contains("世界观") || t.Contains("世界规则") || t.Contains("修炼") || t.Contains("体系")
                || t.Contains("设定") || t.Contains("金手指")
                || t.Contains("能力") || t.Contains("技能") || t.Contains("法术"))
                return new[] { "SearchWorldRules", "GetWorldRuleById", "GetWorldRulesByIds" };

            if (t.Contains("势力") || t.Contains("门派") || t.Contains("阵营")
                || t.Contains("组织") || t.Contains("家族"))
                return new[] { "SearchFactions", "GetFactionById", "GetFactionsByIds" };

            if (t.Contains("地点") || t.Contains("场景") || t.Contains("位置") || t.Contains("地图"))
                return new[] { "SearchLocations", "GetLocationById", "GetLocationsByIds" };

            if (t.Contains("情节") || t.Contains("剧情") || t.Contains("冲突")
                || t.Contains("伏笔") || t.Contains("线索") || t.Contains("悬念") || t.Contains("转折"))
                return new[] { "SearchPlotRules", "GetPlotRuleById", "GetPlotRulesByIds" };

            if (t.Contains("规则") || t.Contains("约束") || t.Contains("硬约束"))
                return new[] { "SmartSearch" };

            if (t.Contains("项目") || t.Contains("作品") || t.Contains("小说")
                || t.Contains("书") || t.Contains("故事"))
                return new[] { "GetProjectContext" };

            if (t.Contains("数据") || t.Contains("知识库") || t.Contains("索引"))
                return new[] { "SmartSearch" };

            if (t.Contains("校验") || t.Contains("验证") || t.Contains("打包") || t.Contains("发布"))
                return new[] { "ValidateDataConsistency" };

            return null;
        }

        private async Task<string?> OrchestrateBusinessQueryAsync(string userText)
        {
            try
            {
                var routing = ServiceLocator.TryGet<TM.Services.Framework.AI.QueryRouting.QueryRoutingService>();
                if (routing == null) return null;

                var query = ExtractQueryKeywords(userText);
                if (string.IsNullOrWhiteSpace(query)) query = userText.Trim();
                var t = userText.Trim();

                string? searchResult = null;
                Func<string, Task<string>>? getByIdFunc = null;

                if (t.Contains("素材") || t.Contains("模板") || t.Contains("创作")
                    || t.Contains("文风") || t.Contains("风格") || t.Contains("流派") || t.Contains("题材")
                    || t.Contains("灵感") || t.Contains("构思")
                    || t.Contains("拆书") || t.Contains("分析结果")
                    || t.Contains("塑造") || t.Contains("人物关系") || t.Contains("关系图"))
                {
                    var cmPlugin = new TM.Services.Framework.AI.SemanticKernel.Plugins.DataLookupPlugin();
                    searchResult = await cmPlugin.SearchCreativeMaterialsAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = cmPlugin.GetCreativeMaterialByIdAsync;
                }
                else if (t.Contains("角色") || t.Contains("主角") || t.Contains("配角")
                         || t.Contains("人物") || t.Contains("反派") || t.Contains("龙套"))
                {
                    if (t.Contains("主角") && !t.Contains("素材"))
                    {
                        var plugin = new TM.Services.Framework.AI.SemanticKernel.Plugins.DataLookupPlugin();
                        searchResult = await plugin.GetProtagonistsAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        searchResult = await routing.SearchCharactersAsync(query, 5).ConfigureAwait(false);
                    }
                    getByIdFunc = routing.GetCharacterByIdAsync;
                }
                else if (t.Contains("世界观") || t.Contains("世界规则") || t.Contains("修炼") || t.Contains("体系")
                         || t.Contains("设定") || t.Contains("金手指")
                         || t.Contains("能力") || t.Contains("技能") || t.Contains("法术"))
                {
                    searchResult = await routing.SearchWorldRulesAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetWorldRuleByIdAsync;
                }
                else if (t.Contains("势力") || t.Contains("门派") || t.Contains("阵营")
                         || t.Contains("组织") || t.Contains("家族"))
                {
                    searchResult = await routing.SearchFactionsAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetFactionByIdAsync;
                }
                else if (t.Contains("地点") || t.Contains("场景") || t.Contains("位置") || t.Contains("地图"))
                {
                    searchResult = await routing.SearchLocationsAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetLocationByIdAsync;
                }
                else if (t.Contains("情节") || t.Contains("剧情") || t.Contains("冲突")
                         || t.Contains("伏笔") || t.Contains("线索") || t.Contains("悬念") || t.Contains("转折"))
                {
                    searchResult = await routing.SearchPlotRulesAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetPlotRuleByIdAsync;
                }
                else if (t.Contains("大纲") || t.Contains("纲要"))
                {
                    searchResult = await routing.SearchOutlinesAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetOutlineByIdAsync;
                }
                else if (t.Contains("分卷") || t.Contains("卷"))
                {
                    searchResult = await routing.SearchVolumeDesignsAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetVolumeDesignByIdAsync;
                }
                else if (t.Contains("章节") || t.Contains("章"))
                {
                    searchResult = await routing.SearchChapterPlansAsync(query, 10).ConfigureAwait(false);
                    getByIdFunc = routing.GetChapterPlanByIdAsync;
                }
                else if (t.Contains("蓝图"))
                {
                    searchResult = await routing.SearchBlueprintsAsync(query, 5).ConfigureAwait(false);
                    getByIdFunc = routing.GetBlueprintByIdAsync;
                }
                else if (t.Contains("正文") || t.Contains("字数") || t.Contains("篇幅")
                         || t.Contains("已生成") || t.Contains("未生成") || t.Contains("生成进度"))
                {
                    var contentPlugin = new TM.Services.Framework.AI.SemanticKernel.Plugins.DataLookupPlugin();
                    if (t.Contains("未生成") || t.Contains("没写") || t.Contains("还没"))
                    {
                        searchResult = await contentPlugin.ListUngeneratedChaptersAsync().ConfigureAwait(false);
                    }
                    else if (t.Contains("已生成") && !t.Contains("字数") && !t.Contains("统计"))
                    {
                        searchResult = await contentPlugin.ListGeneratedChaptersAsync().ConfigureAwait(false);
                    }
                    else if (t.Contains("搜索") || t.Contains("查找") || t.Contains("提到") || t.Contains("出现"))
                    {
                        searchResult = await routing.SearchContentAsync(query, 5).ConfigureAwait(false);
                    }
                    else if (t.Contains("相关") || t.Contains("涉及"))
                    {
                        searchResult = await routing.FindRelatedChaptersAsync(query).ConfigureAwait(false);
                    }
                    else
                    {
                        searchResult = await contentPlugin.GetGeneratedChaptersSummaryAsync().ConfigureAwait(false);
                    }
                }
                else if (t.Contains("校验") || t.Contains("验证") || t.Contains("打包") || t.Contains("发布"))
                {
                    searchResult = await routing.ValidateDataConsistencyAsync().ConfigureAwait(false);
                }
                else if (t.Contains("项目") || t.Contains("作品") || t.Contains("小说")
                         || t.Contains("书") || t.Contains("故事"))
                {
                    searchResult = await ProjectContextBuilder.BuildAsync().ConfigureAwait(false);
                }
                else
                {
                    searchResult = await routing.SmartSearchAsync(query).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(searchResult))
                    return null;

                var sb = new StringBuilder();
                sb.AppendLine("[搜索结果]");
                sb.AppendLine(searchResult);

                if (getByIdFunc != null && !searchResult.Contains("[未找到]"))
                {
                    var ids = ExtractIdsFromResult(searchResult);
                    if (ids.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[详细信息]");
                        foreach (var id in ids.Take(3))
                        {
                            try
                            {
                                var detail = await getByIdFunc(id).ConfigureAwait(false);
                                if (!string.IsNullOrWhiteSpace(detail))
                                {
                                    sb.AppendLine(detail);
                                    sb.AppendLine();
                                }
                            }
                            catch { }
                        }
                    }
                }

                var finalResult = sb.ToString().Trim();

                if (finalResult.Length > 6000)
                    finalResult = finalResult[..6000] + "\n...(数据已截断，可追问获取更多)";

                TM.App.Log($"[SKConversationViewModel] 查询编排完成: {finalResult.Length} 字符");
                return finalResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 查询编排异常: {ex.Message}");
                return null;
            }
        }

        private static List<string> ExtractIdsFromResult(string result)
        {
            var ids = ExtractIdsFromJson(result);
            if (ids.Count > 0) return ids;
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(result, @"\(([a-zA-Z0-9_\-]{3,})\)"))
            {
                var id = m.Groups[1].Value;
                if (!ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }

        private static string ExtractQueryKeywords(string userText)
        {
            var t = userText.Trim();
            string[] prefixes = { "请问", "告诉我", "查一下", "帮我查", "帮我看看", "我想知道", "我想了解", "查询一下", "查询", "搜索", "查看", "看看" };
            foreach (var p in prefixes)
            {
                if (t.StartsWith(p)) { t = t[p.Length..].TrimStart(); break; }
            }
            t = t.TrimEnd('？', '?', '。', '.', '！', '!', '，', ',');
            string[] suffixes = { "是什么", "是谁", "有什么", "有哪些", "有多少", "是啥", "怎么样", "什么样", "都有谁", "都有啥" };
            foreach (var s in suffixes)
            {
                if (t.EndsWith(s)) { t = t[..^s.Length].TrimEnd(); break; }
            }
            if (t.EndsWith("的")) t = t[..^1];
            return t.Trim();
        }

        private static List<string> ExtractIdsFromJson(string jsonResult)
        {
            var ids = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(jsonResult);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.TryGetProperty("Id", out var idProp)
                            || elem.TryGetProperty("id", out idProp))
                        {
                            var id = idProp.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                                ids.Add(id);
                        }
                    }
                }
            }
            catch
            {
            }
            return ids;
        }

    }
}

