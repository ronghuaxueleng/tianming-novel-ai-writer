using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Services;
using TM.Services.Framework.AI;
using TM.Framework.User.Services;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ModelManagementViewModel
{
    private string? _pendingCategoryName;

    private void LogScoped(string message) => _currentEditingCategory.LogIfPublic(message);

    private async Task FetchModelsAsync()
    {
        using var _epScope = EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate());
        _testConnectionCts?.Cancel();
        _testConnectionCts?.Dispose();
        _testConnectionCts = new CancellationTokenSource();
        var token = _testConnectionCts.Token;

        _testingProgressText = "正在获取模型...";
        IsTestingConnection = true;
        try
        {
            if (!IsAutoFetchMode)
            {
                StandardDialog.ShowWarning("请切换到「自动获取端点内所有模型」模式", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiEndpoint))
            {
                StandardDialog.ShowWarning("请先输入API端点", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiKey))
            {
                StandardDialog.ShowWarning("请先输入API密钥", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            var expectedSig = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);
            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint) ||
                string.IsNullOrWhiteSpace(_currentEditingCategory.EndpointSignature) ||
                !string.Equals(_currentEditingCategory.EndpointSignature, expectedSig, StringComparison.OrdinalIgnoreCase))
            {
                StandardDialog.ShowWarning("请先点击「测试连接」验证端点", "提示");
                return;
            }

            GlobalToast.Info("获取模型", "正在从API获取可用模型列表...");
            LogScoped($"[ModelManagement] 开始获取全部模型: Category={FormCategory}, ModelsEndpoint={_currentEditingCategory.ModelsEndpoint}");

            token.ThrowIfCancellationRequested();
            var models = await FetchModelsFromApiAsync(_currentEditingCategory.ModelsEndpoint, FormApiKey, token);

            if (models == null || models.Count == 0)
            {
                StandardDialog.ShowWarning("未获取到模型列表，请检查API端点和密钥是否正确", "提示");
                return;
            }

            AvailableModels.ReplaceAll(models.ToList());

            OnPropertyChanged(nameof(IsModelComboEnabled));

            LogScoped($"[ModelManagement] 获取模型成功: {AvailableModels.Count}个");

            var result = StandardDialog.ShowConfirm(
                $"成功获取到 {models.Count} 个模型，是否要为这些模型批量创建配置？\n\n创建后的配置将显示在「{FormCategory}」分类下。",
                "批量创建配置");

            if (result)
            {
                await BatchCreateConfigurationsAsync(models);
            }
            else
            {
                GlobalToast.Success("获取成功", $"获取到 {AvailableModels.Count} 个可用模型");
            }
        }
        catch (OperationCanceledException)
        {
            LogScoped("[ModelManagement] 获取模型已取消");
            GlobalToast.Info("已取消", "获取模型已取消");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 获取模型失败: {ex.Message}");
            StandardDialog.ShowError($"无法获取模型列表\n\n错误详情：{ex.Message}", "获取失败");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private async Task FetchManualModelAsync()
    {
        using var _epScope = EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate());
        _testConnectionCts?.Cancel();
        _testConnectionCts?.Dispose();
        _testConnectionCts = new CancellationTokenSource();

        _testingProgressText = "正在获取模型...";
        IsTestingConnection = true;
        try
        {
            if (!IsManualInputMode)
            {
                StandardDialog.ShowWarning("请切换到「手动获取输入指定模型」模式", "提示");
                return;
            }

            var manualName = ManualModelName?.Trim();
            if (string.IsNullOrWhiteSpace(manualName))
            {
                StandardDialog.ShowWarning("请输入模型名称", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiEndpoint))
            {
                StandardDialog.ShowWarning("请先输入API端点", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(FormApiKey))
            {
                StandardDialog.ShowWarning("请先输入API密钥", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            var expectedSigManual = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);
            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint) ||
                string.IsNullOrWhiteSpace(_currentEditingCategory.EndpointSignature) ||
                !string.Equals(_currentEditingCategory.EndpointSignature, expectedSigManual, StringComparison.OrdinalIgnoreCase))
            {
                StandardDialog.ShowWarning("请先点击「测试连接」验证端点", "提示");
                return;
            }

            LogScoped($"[ModelManagement] 手动添加模型: {manualName}");
            GlobalToast.Info("添加模型", $"正在创建模型配置: {manualName}");

            var manualModel = new ModelInfo
            {
                Id = manualName,
                Name = manualName,
                ContextLength = 0,
                MaxTokens = 0
            };

            AvailableModels.ReplaceAll(new List<ModelInfo> { manualModel });
            OnPropertyChanged(nameof(IsModelComboEnabled));

            LogScoped($"[ModelManagement] 手动模型已就绪: {manualName}");

            var result = StandardDialog.ShowConfirm(
                $"确认添加模型「{manualName}」？\n\n该配置将显示在「{FormCategory}」分类下。\n注意：模型名称须与供应商端点实际支持的 model id 一致。",
                "添加模型");

            if (result)
            {
                await BatchCreateConfigurationsAsync(new List<ModelInfo> { manualModel });
            }
            else
            {
                GlobalToast.Info("已取消", $"取消添加模型: {manualName}");
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 手动获取模型失败: {ex.Message}");
            StandardDialog.ShowError($"无法获取模型\n\n错误详情：{ex.Message}", "获取失败");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private static readonly string[] FallbackTestModels =
    {
        "qwen-plus", "qwen-turbo", "qwen-long", "qwen-max", "qwen-coder-plus",
        "qwen3.5-plus", "qwen3-coder-plus",
        "deepseek-chat", "deepseek-v3", "deepseek-coder",
        "moonshot-v1-8k",
        "glm-4", "glm-4-flash", "glm-5", "codegeex-4",
        "yi-lightning", "yi-large",
        "ep-*",
        "abab6.5s-chat",
        "lite",
        "step-1-8k",
        "ERNIE-4.0-8K",
        "Qwen/Qwen2.5-7B-Instruct",
        "gpt-4o-mini", "gpt-4o", "gpt-4",
        "claude-haiku", "claude-3-haiku",
        "gemini-flash", "gemini-1.5-flash",
        "llama-3.1-8b-instant",
        "meta-llama/Llama-3.2-3B-Instruct-Turbo"
    };

    private async Task TestApiConnectionAsync()
    {
        using var _epScope = EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate());
        if (string.IsNullOrWhiteSpace(FormApiEndpoint))
        {
            StandardDialog.ShowWarning("请先输入API端点", "提示");
            return;
        }

        if (string.IsNullOrWhiteSpace(FormApiKey))
        {
            StandardDialog.ShowWarning("请先输入API密钥", "提示");
            return;
        }

        if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
        {
            StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
            return;
        }

        if (_currentEditingCategory.IsBuiltIn)
        {
            if (!await VerifyBuiltInAccessPasswordAsync())
                return;
        }
        else
        {
            var confirmedSave = StandardDialog.ShowConfirm(
                "测试连接前必须先保存当前配置。\n是否保存后继续测试？",
                "测试前保存");
            if (!confirmedSave)
                return;

            bool saved;
            try
            {
                await Service.EnsureInitializedAsync().ConfigureAwait(true);
                saved = ExecuteSaveWithCreateEditMode(
                    validateForm: ValidateFormCore,
                    createCategoryCore: CreateCategoryCore,
                    createDataCore: CreateDataCore,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCore,
                    updateDataCore: UpdateDataCore);
            }
            catch (Exception saveEx)
            {
                LogScoped($"[ModelManagement] 测试前保存失败: {saveEx.Message}");
                StandardDialog.ShowError($"保存失败，无法继续测试：\n{saveEx.Message}", "测试中止");
                return;
            }

            if (!saved)
            {
                return;
            }
        }

        _testConnectionCts?.Cancel();
        _testConnectionCts?.Dispose();
        _testConnectionCts = new CancellationTokenSource();
        var token = _testConnectionCts.Token;

        _testingProgressText = "正在测试连接...";
        IsTestingConnection = true;
        StartTimeoutMonitor(_testConnectionCts);
        try
        {
            _chatTestFailed = false;
            ChatRetryModels.Clear();
            RefreshEndpointVerificationStatus();

            GlobalToast.Info("测试连接", "正在测试端点...");
            LogScoped($"[ModelManagement] 开始端点测试: Endpoint={FormApiEndpoint}");

            var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);
            LogScoped($"[ModelManagement] 候选URL: {string.Join(", ", candidates)}");

            token.ThrowIfCancellationRequested();

            var modelsResult = await _endpointTestService.TestModelsEndpointAsync(candidates, FormApiKey, token);

            if (modelsResult.Success)
            {
                LogScoped($"[ModelManagement] Models 端点成功: {modelsResult.SuccessfulEndpoint}, 模型数: {modelsResult.Models.Count}");
                _lastFetchedModels = modelsResult.Models;
                _currentEditingCategory.ModelsEndpoint = modelsResult.SuccessfulEndpoint;

                var testModelId = _endpointTestService.SelectTestModel(modelsResult.Models);
                if (string.IsNullOrWhiteSpace(testModelId))
                {
                    StandardDialog.ShowWarning("未找到可用于测试的模型", "测试结果");
                    return;
                }

                token.ThrowIfCancellationRequested();
                GlobalToast.Info("测试连接", $"正在测试 Chat 端点（模型: {testModelId}）...");
                var chatResult = await _endpointTestService.TestChatEndpointAsync(candidates, FormApiKey, testModelId, token);

                if (!chatResult.Success)
                {
                    LogScoped($"[ModelManagement] Chat 端点测试失败: {chatResult.ErrorMessage}");
                    _chatTestFailed = true;
                    ChatRetryModels.ReplaceAll(modelsResult.Models.ToList());
                    RefreshEndpointVerificationStatus();

                    var chatErrorHint = EndpointTestService.GetErrorTypeDisplayName(chatResult.ErrorType);
                    var saveAnyway = StandardDialog.ShowConfirm(
                        $"Chat 端点测试失败\n\n" +
                        $"原因：{chatErrorHint}\n\n" +
                        $"详细信息：{chatResult.ErrorMessage}\n\n" +
                        $"但 Models 端点已成功获取 {modelsResult.Models.Count} 个模型，端点地址有效。\n\n" +
                        $"可能原因：测试所用模型需要特定的 API 密钥（如本地中转服务）。\n\n" +
                        $"是否直接保存端点配置？（也可点击\"取消\"后在下方选择其他模型重试）",
                        "Chat 测试失败 - 是否直接保存？");

                    if (saveAnyway)
                    {
                        SaveVerifiedEndpoints(modelsResult.SuccessfulEndpoint, modelsResult.SuccessfulEndpoint, modelsResult.Models.Count);
                    }
                    return;
                }

                SaveVerifiedEndpoints(modelsResult.SuccessfulEndpoint, chatResult.SuccessfulEndpoint, modelsResult.Models.Count);
                return;
            }

            bool isWafBlock = modelsResult.ErrorType == EndpointErrorType.WafBlock;
            bool isNotFound = modelsResult.ErrorType == EndpointErrorType.NotFound;

            LogScoped($"[ModelManagement] Models 端点失败: {modelsResult.ErrorMessage}, ErrorType={modelsResult.ErrorType}, WAF={isWafBlock}, NotFound={isNotFound}");

            if (!isWafBlock && !isNotFound)
            {
                var errorHint = EndpointTestService.GetErrorTypeDisplayName(modelsResult.ErrorType);
                StandardDialog.ShowWarning($"Models 端点测试失败\n\n原因：{errorHint}\n\n详细信息：{modelsResult.ErrorMessage}", "测试结果");
                return;
            }

            if (isNotFound)
            {
                LogScoped("[ModelManagement] Models 端点404，端点可能不暴露/models（如 coding.dashscope），降级测 Chat");
                GlobalToast.Info("测试连接", "Models 端点不支持，降级测试 Chat 端点...");

                ChatTestResult? successResult = null;
                foreach (var fallbackModel in FallbackTestModels)
                {
                    token.ThrowIfCancellationRequested();
                    var tryResult = await _endpointTestService.TestChatEndpointAsync(
                        candidates, FormApiKey, fallbackModel, token);
                    LogScoped($"[ModelManagement] 降级尝试模型 {fallbackModel}: {(tryResult.Success ? "成功" : tryResult.ErrorMessage + (tryResult.RawErrorBody != null ? " | 响应体: " + tryResult.RawErrorBody[..Math.Min(200, tryResult.RawErrorBody.Length)] : string.Empty))}");
                    if (tryResult.Success) { successResult = tryResult; break; }
                }

                if (successResult != null)
                {
                    var baseUrl = successResult.SuccessfulEndpoint ?? candidates.FirstOrDefault() ?? FormApiEndpoint.TrimEnd('/');
                    SaveVerifiedEndpoints(baseUrl, baseUrl, 0);
                    return;
                }

                LogScoped("[ModelManagement] 所有 FallbackTestModels 均失败");
                var saveFailed404 = StandardDialog.ShowConfirm(
                    "端点不支持 /models 列表，且所有模型候选均测试失败。\n\n" +
                    "如果你在其他客户端（如 Cherry Studio）能用此端点，请确认 API Key 正确、模型 ID 存在。\n\n" +
                    "是否仍然保存此端点配置？\n\n" +
                    "保存后请切换到「手动获取输入指定模型」，手动输入模型 ID 添加模型。",
                    "测试失败 - 是否强制保存？");
                if (saveFailed404)
                {
                    var baseUrl = candidates.FirstOrDefault() ?? FormApiEndpoint.TrimEnd('/');
                    SaveVerifiedEndpoints(baseUrl, baseUrl, 0);
                }
                return;
            }

            LogScoped("[ModelManagement] WAF保护端点，跳过 Chat 原始测试，直接进入强制保存流程");

            LogScoped("[ModelManagement] WAF保护端点，强制保存流程");
            var trustSave = StandardDialog.ShowConfirm(
                "端点受 WAF 安全防护，自动验证无法绕过。\n\n" +
                "可能原因：\n" +
                "• 端点受 Cloudflare 等 WAF 保护，需要特定客户端\n" +
                "• API 密钥无效或权限不足\n" +
                "• 端点 URL 格式不正确\n\n" +
                "是否仍然保存此端点配置（信任用户填写的 URL）？\n\n" +
                "保存后请切换到「手动获取输入指定模型」，手动输入模型 ID 添加模型。",
                "验证失败 - 是否强制保存？");

            if (trustSave)
            {
                var baseUrl = candidates.FirstOrDefault() ?? FormApiEndpoint.TrimEnd('/');
                SaveVerifiedEndpoints(baseUrl, baseUrl, 0);
                GlobalToast.Warning("强制保存", "端点已保存，请手动添加模型名称");
            }
        }
        catch (TaskCanceledException)
        {
            LogScoped("[ModelManagement] API连接测试超时/取消");
            GlobalToast.Info("已取消", "测试连接已取消");
        }
        catch (OperationCanceledException)
        {
            LogScoped("[ModelManagement] API连接测试已取消");
            GlobalToast.Info("已取消", "测试连接已取消");
        }
        catch (HttpRequestException ex)
        {
            LogScoped($"[ModelManagement] API连接测试网络错误: {ex.Message}");
            StandardDialog.ShowError($"网络连接失败\n\n错误详情：{ex.Message}\n\n请检查：\n• 端点地址是否正确\n• 网络/代理是否正常\n• 端点服务是否可用", "连接失败");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] API连接测试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败\n\n错误详情：{ex.Message}", "错误");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private void SaveVerifiedEndpoints(string? modelsEndpoint, string? chatEndpoint, int modelCount)
    {
        if (_currentEditingCategory == null) return;

        var oldChatEndpoint = _currentEditingCategory.ChatEndpoint;

        _currentEditingCategory.ModelsEndpoint = modelsEndpoint ?? string.Empty;
        _currentEditingCategory.ChatEndpoint = chatEndpoint ?? string.Empty;
        _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
        _currentEditingCategory.EndpointSignature =
            _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

        EndpointTestService.InvalidateProbeCache(oldChatEndpoint);
        if (!string.Equals(oldChatEndpoint, chatEndpoint, StringComparison.OrdinalIgnoreCase))
            EndpointTestService.InvalidateProbeCache(chatEndpoint);

        Service.UpdateCategory(_currentEditingCategory);

        if (_currentEditingCategory.IsBuiltIn)
        {
            Service.SaveAllCategories();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
                await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
        });

        _chatTestFailed = false;
        ChatRetryModels.Clear();
        RefreshEndpointVerificationStatus();

        try
        {
            var providerId = _currentEditingCategory.Id;
            var newEndpoint = _currentEditingCategory.ChatEndpoint ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(newEndpoint))
            {
                var affectedConfigs = _aiConfigurationService.GetAllConfigurations()
                    .Where(c => string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(c.CustomEndpoint ?? string.Empty, newEndpoint, StringComparison.Ordinal))
                    .ToList();
                foreach (var cfg in affectedConfigs)
                {
                    var oldEndpoint = cfg.CustomEndpoint ?? string.Empty;
                    cfg.CustomEndpoint = newEndpoint;
                    _aiConfigurationService.UpdateConfiguration(cfg);
                    LogScoped($"[ModelManagement] 端点变更已同步到对话配置: {cfg.Name}, {oldEndpoint} → {newEndpoint}");
                }
                if (affectedConfigs.Count > 0)
                {
                    GlobalToast.Info("端点已同步", $"已将新端点同步到 {affectedConfigs.Count} 个对话配置");
                }
            }
        }
        catch (Exception syncEx)
        {
            LogScoped($"[ModelManagement] 同步 CustomEndpoint 到对话配置失败: {syncEx.Message}");
        }

        LogScoped($"[ModelManagement] 端点已保存: Models={modelsEndpoint}, Chat={chatEndpoint}, 模型数={modelCount}");

        bool hideEndpoints = _currentEditingCategory?.IsBuiltIn == true;

        if (modelCount > 0)
        {
            var msg = hideEndpoints
                ? $"端点测试通过！\n\n可用模型数: {modelCount}"
                : $"端点测试通过！\n\nModels 端点: {modelsEndpoint}\nChat 端点: {chatEndpoint}\n可用模型数: {modelCount}";
            StandardDialog.ShowInfo(msg, "测试成功");
            GlobalToast.Success("连接成功", "API端点验证通过");
        }
        else
        {
            var msg = hideEndpoints
                ? "端点连接成功！\n\n请切换到「手动获取输入指定模型」，手动输入模型 ID 添加模型。"
                : $"端点连接成功！\n\nChat 端点: {chatEndpoint}\n\n" +
                  $"该端点不提供 /models 列表（如阿里云 Coding Plan coding.dashscope.aliyuncs.com）。\n\n" +
                  $"请切换到「手动获取输入指定模型」，手动输入模型 ID 添加模型。\n" +
                  $"Coding Plan 可用模型 ID 示例：\n  qwen3.5-plus / qwen3-coder-plus / qwen-coder-plus";
            StandardDialog.ShowInfo(msg, hideEndpoints ? "测试成功" : "测试成功（需手动添加模型）");
            GlobalToast.Success("连接成功", hideEndpoints ? "端点验证通过" : "端点验证通过，请手动添加模型");
        }
    }

    private async Task RetryChatTestAsync()
    {
        using var _epScope = EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate());
        try
        {
            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (SelectedChatRetryModel == null)
            {
                StandardDialog.ShowWarning("请选择一个模型进行测试", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            var testModelId = SelectedChatRetryModel.Id;
            if (string.IsNullOrWhiteSpace(testModelId))
            {
                StandardDialog.ShowWarning("所选模型ID为空，请选择其他模型", "提示");
                return;
            }
            LogScoped($"[ModelManagement] 用户选择模型重试 Chat 测试: {testModelId}");
            GlobalToast.Info("重试测试", $"正在测试 Chat 端点（模型: {testModelId}）...\n注意：此测试会消耗极少量 token");

            var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);

            var chatResult = await _endpointTestService.TestChatEndpointAsync(
                candidates, FormApiKey, testModelId);

            if (!chatResult.Success)
            {
                LogScoped($"[ModelManagement] Chat 端点重试测试失败: ErrorType={chatResult.ErrorType}, {chatResult.ErrorMessage}");
                var chatErrorHint = EndpointTestService.GetErrorTypeDisplayName(chatResult.ErrorType);
                StandardDialog.ShowWarning(
                    $"Chat 端点测试失败\n\n原因：{chatErrorHint}\n\n详细信息：{chatResult.ErrorMessage}\n\n请尝试选择其他模型。",
                    "测试结果");
                return;
            }

            LogScoped($"[ModelManagement] Chat 端点重试测试成功: {chatResult.SuccessfulEndpoint}");

            var oldChatEndpointRetry = _currentEditingCategory.ChatEndpoint;
            _currentEditingCategory.ChatEndpoint = chatResult.SuccessfulEndpoint;
            _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
            _currentEditingCategory.EndpointSignature = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

            EndpointTestService.InvalidateProbeCache(oldChatEndpointRetry);
            if (!string.Equals(oldChatEndpointRetry, chatResult.SuccessfulEndpoint, StringComparison.OrdinalIgnoreCase))
                EndpointTestService.InvalidateProbeCache(chatResult.SuccessfulEndpoint);

            Service.UpdateCategory(_currentEditingCategory);
            if (_currentEditingCategory.IsBuiltIn)
            {
                Service.SaveAllCategories();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
                    await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
            });

            _chatTestFailed = false;
            ChatRetryModels.Clear();
            SelectedChatRetryModel = null;
            RefreshEndpointVerificationStatus();

            var resultMessage = _currentEditingCategory.IsBuiltIn
                ? "端点测试通过！"
                : $"端点测试通过！\n\n" +
                  $"Models 端点: {_currentEditingCategory.ModelsEndpoint}\n" +
                  $"Chat 端点: {chatResult.SuccessfulEndpoint}";

            StandardDialog.ShowInfo(resultMessage, "测试成功");
            GlobalToast.Success("连接成功", "API端点验证通过");
        }
        catch (TaskCanceledException)
        {
            LogScoped("[ModelManagement] Chat 重试测试超时");
            StandardDialog.ShowWarning("连接超时，请检查网络", "测试结果");
        }
        catch (HttpRequestException ex)
        {
            LogScoped($"[ModelManagement] Chat 重试测试网络错误: {ex.Message}");
            StandardDialog.ShowError($"网络连接失败\n\n错误详情：{ex.Message}", "连接失败");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] Chat 重试测试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败\n\n错误详情：{ex.Message}", "错误");
        }
    }

    private async Task RetryWithDropdownAsync()
    {
        try
        {
            if (!IsAutoRetryMode)
            {
                StandardDialog.ShowWarning("请切换到「获取模型重试」模式", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            if (SelectedChatRetryModel == null || string.IsNullOrWhiteSpace(SelectedChatRetryModel.Id))
            {
                StandardDialog.ShowWarning("请从下拉列表中选择一个模型", "提示");
                return;
            }

            var testModelId = SelectedChatRetryModel.Id;
            await ExecuteChatRetryTestAsync(testModelId);
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 下拉重试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败\n\n错误详情：{ex.Message}", "错误");
        }
    }

    private async Task RetryWithManualAsync()
    {
        try
        {
            if (!IsManualRetryMode)
            {
                StandardDialog.ShowWarning("请切换到「指定模型重试」模式", "提示");
                return;
            }

            if (_currentEditingCategory == null || _currentEditingCategory.Level != 2)
            {
                StandardDialog.ShowWarning("请先选择一个供应商分类", "提示");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentEditingCategory.ModelsEndpoint))
            {
                StandardDialog.ShowWarning("请先完成 Models 端点测试", "提示");
                return;
            }

            var testModelId = RetryManualModelName?.Trim();
            if (string.IsNullOrWhiteSpace(testModelId))
            {
                StandardDialog.ShowWarning("请输入模型名称", "提示");
                return;
            }

            await ExecuteChatRetryTestAsync(testModelId);
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 手动重试异常: {ex.Message}");
            StandardDialog.ShowError($"测试失败\n\n错误详情：{ex.Message}", "错误");
        }
    }

    private async Task ExecuteChatRetryTestAsync(string testModelId)
    {
        using var _epScope = EndpointTestService.BeginPrivateScope(_currentEditingCategory.IsTianmingPrivate());
        LogScoped($"[ModelManagement] 重试 Chat 测试: {testModelId}");
        GlobalToast.Info("重试测试", $"正在测试 Chat 端点（模型: {testModelId}）...\n注意：此测试会消耗极少量 token");

        var candidates = _endpointTestService.GenerateCandidateUrls(FormApiEndpoint);

        var chatResult = await _endpointTestService.TestChatEndpointAsync(
            candidates, FormApiKey, testModelId);

        if (!chatResult.Success)
        {
            LogScoped($"[ModelManagement] Chat 端点重试测试失败: ErrorType={chatResult.ErrorType}, {chatResult.ErrorMessage}");
            var chatErrorHint = EndpointTestService.GetErrorTypeDisplayName(chatResult.ErrorType);
            StandardDialog.ShowWarning(
                $"Chat 端点测试失败\n\n原因：{chatErrorHint}\n\n详细信息：{chatResult.ErrorMessage}\n\n请尝试其他模型。",
                "测试结果");
            return;
        }

        LogScoped($"[ModelManagement] Chat 端点重试测试成功: {chatResult.SuccessfulEndpoint}");

        var oldChatEndpointExec = _currentEditingCategory!.ChatEndpoint;
        _currentEditingCategory.ChatEndpoint = chatResult.SuccessfulEndpoint;
        _currentEditingCategory.EndpointVerifiedAt = DateTime.Now;
        _currentEditingCategory.EndpointSignature = _endpointTestService.ComputeEndpointSignature(FormApiEndpoint, FormApiKey);

        EndpointTestService.InvalidateProbeCache(oldChatEndpointExec);
        if (!string.Equals(oldChatEndpointExec, chatResult.SuccessfulEndpoint, StringComparison.OrdinalIgnoreCase))
            EndpointTestService.InvalidateProbeCache(chatResult.SuccessfulEndpoint);

        Service.UpdateCategory(_currentEditingCategory);
        if (_currentEditingCategory.IsBuiltIn)
        {
            Service.SaveAllCategories();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
                await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
        });

        _chatTestFailed = false;
        ChatRetryModels.Clear();
        SelectedChatRetryModel = null;
        RefreshEndpointVerificationStatus();

        var resultMessage = _currentEditingCategory.IsBuiltIn
            ? "端点测试通过！"
            : $"端点测试通过！\n\n" +
              $"Models 端点: {_currentEditingCategory.ModelsEndpoint}\n" +
              $"Chat 端点: {chatResult.SuccessfulEndpoint}";

        StandardDialog.ShowInfo(resultMessage, "测试成功");
        GlobalToast.Success("连接成功", "API端点验证通过");
    }

    private async Task BatchCreateConfigurationsAsync(List<ModelInfo> models)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                StandardDialog.ShowWarning("请先选择所属分类", "提示");
                return;
            }

            var providerCategory = Service.GetAllCategories()
                .FirstOrDefault(c => c.Name == FormCategory && c.Level == 2);
            var providerLogoPath = providerCategory?.LogoPath
                ?? TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(FormCategory);
            var providerIcon = providerCategory?.Icon ?? FormIcon ?? "Icon.Robot";

            GlobalToast.Info("批量创建", $"正在创建 {models.Count} 个模型配置...");

            var categoryName = FormCategory;
            var apiEndpoint = FormApiEndpoint;
            var apiKey = FormApiKey;

            var (configsToAdd, skipCount) = await Task.Run(() =>
            {
                var configs = new List<UserConfigurationData>();
                var existingModelNames = new HashSet<string>(
                    Service.GetAllData()
                        .Where(d => d.Category == categoryName)
                        .Select(d => d.ModelName));
                int skipped = 0;

                foreach (var model in models)
                {
                    if (existingModelNames.Contains(model.Id))
                    {
                        skipped++;
                        continue;
                    }

                    var modelLogoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(model.Id)
                        ?? providerLogoPath;
                    var effectiveContextLength = model.ContextLength;

                    var importMaxTokens = model.MaxTokens;

                    configs.Add(new UserConfigurationData
                    {
                        Name = model.Name,
                        ModelName = model.Id,
                        Icon = modelLogoPath ?? providerIcon,
                        Category = categoryName,
                        Description = model.Description ?? $"模型ID: {model.Id}",
                        IsEnabled = false,
                        ApiEndpoint = apiEndpoint,
                        ApiKey = apiKey,
                        MaxTokens = importMaxTokens,
                        ContextLength = effectiveContextLength > 0 ? effectiveContextLength.ToString() : "",
                        Temperature = 0.7,
                        TopP = 1.0,
                        SupportsReasoningEffort = model.SupportsReasoningEffort,
                        SupportedEffortLevels = model.SupportedEffortLevels,
                        SupportsThinking = model.SupportsThinking,
                        SupportsVision = model.SupportsVision,
                        SupportsImageGeneration = model.SupportsImageGeneration,
                        SupportsTools = model.SupportsTools,
                        SupportsStreaming = model.SupportsStreaming,
                        CapabilitiesDetected = model.CapabilitiesDetected
                    });
                }

                return (configs, skipped);
            });

            int successCount = 0;
            if (configsToAdd.Count > 0)
            {
                _suppressTreeRefreshCount++;
                successCount = await Task.Run(() => Service.AddConfigurationsBatch(configsToAdd, categoryName));

                var providerNode = FindProviderNodeInTree(categoryName);
                if (providerNode != null)
                {
                    foreach (var config in configsToAdd)
                    {
                        var childNode = ConvertToTreeNode(config);
                        childNode.Level = providerNode.Level + 1;
                        providerNode.Children.Add(childNode);
                    }
                }
            }

            GlobalToast.Success("批量创建完成",
                $"成功创建 {successCount} 个配置，跳过 {skipCount} 个已存在的配置");
        }
        catch (Exception ex)
        {
            _suppressTreeRefreshCount = 0;
            LogScoped($"[ModelManagement] 批量创建配置失败: {ex.Message}");
            StandardDialog.ShowError($"批量创建配置失败\n\n错误详情：{ex.Message}", "错误");
        }
    }

    private ICommand? _saveProfilesCommand;
    public ICommand SaveProfilesCommand => _saveProfilesCommand ??= new RelayCommand(_ =>
    {
        try
        {
            Service.SaveParameterProfilesFromUI(ParameterProfiles);
            LoadParameterProfilesForUI();
            GlobalToast.Success("保存成功", "参数模板已更新");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 保存参数模板失败: {ex.Message}");
            GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
        }
    });

    private ICommand? _applyProfileToAllModelsCommand;
    public ICommand ApplyProfileToAllModelsCommand => _applyProfileToAllModelsCommand ??= new RelayCommand(_ =>
    {
        try
        {
            if (_currentProvider == null)
            {
                GlobalToast.Warning("操作无效", "请先选择具体供应商或其模型配置");
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedProfileId))
            {
                GlobalToast.Warning("操作无效", "请先选择要应用的参数模板");
                return;
            }

            var providerName = _currentProvider.Name;
            var profileName = SelectedProfile?.Name ?? SelectedProfileId;

            var confirm = StandardDialog.ShowConfirm(
                $"确定要将参数模板「{profileName}」应用到供应商「{providerName}」下的所有模型吗？\n\n该操作会覆盖这些模型的参数配置字段（Temperature/MaxTokens/TopP等）。",
                "确认批量应用");

            if (!confirm) return;

            Service.ApplyProfileToAllModelsForProvider(_currentProvider, SelectedProfileId);

            if (_currentEditingData != null &&
                _currentProvider != null &&
                _currentEditingData.Category == _currentProvider.Name)
            {
                LoadDataToForm(_currentEditingData);
            }

            GlobalToast.Success("批量应用完成", $"已将模板「{profileName}」应用到供应商「{providerName}」的所有模型。");
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 批量应用参数模板失败: {ex.Message}");
            GlobalToast.Error("批量应用失败", $"批量应用失败：{ex.Message}");
        }
    });

    private async Task<List<ModelInfo>?> FetchModelsFromApiAsync(string apiEndpoint, string apiKey, CancellationToken cancellationToken = default)
    {
        var result = await _endpointTestService.TestModelsEndpointAsync(
            new List<string> { apiEndpoint }, apiKey, cancellationToken);

        if (!result.Success)
        {
            LogScoped($"[ModelManagement] 获取模型失败: {result.ErrorMessage}");
            throw new Exception(result.ErrorMessage ?? "获取模型失败");
        }

        LogScoped($"[ModelManagement] 获取模型成功，共 {result.Models?.Count ?? 0} 个");
        return result.Models?
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new ModelInfo
            {
                Id = m.Id!,
                Name = string.IsNullOrWhiteSpace(m.Name) ? m.Id! : m.Name,
                Description = m.Description,
                MaxTokens = m.MaxTokens > 0 ? m.MaxTokens : 0,
                ContextLength = m.ContextLength,
                SupportsReasoningEffort = m.SupportsReasoningEffort,
                SupportedEffortLevels = m.SupportedEffortLevels,
                SupportsThinking = m.SupportsThinking,
                SupportsVision = m.SupportsVision,
                SupportsImageGeneration = m.SupportsImageGeneration,
                SupportsTools = m.SupportsTools,
                SupportsStreaming = m.SupportsStreaming,
                CapabilitiesDetected = m.CapabilitiesDetected
            })
            .ToList();
    }

    protected async System.Threading.Tasks.Task OnCategoryValueChangedAsync(string? categoryName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return;

            _pendingCategoryName = categoryName;

            await System.Threading.Tasks.Task.Run(
                () => Service.EnsureModelsLoadedForCategory(categoryName))
                .ConfigureAwait(true);

            if (_pendingCategoryName != categoryName) return;

            var provider = Service.GetAllCategories()
                .FirstOrDefault(c => c.Name == categoryName && c.Level == 2);
            SetCurrentProvider(provider);
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] OnCategoryValueChangedAsync 失败: {ex.Message}");
        }
    }

    private void CollectCategoryAndChildren(string categoryName, List<string> result)
    {
        result.Add(categoryName);

        var childCategories = Service.GetAllCategories()
            .Where(c => c.ParentCategory == categoryName)
            .ToList();

        foreach (var child in childCategories)
        {
            CollectCategoryAndChildren(child.Name, result);
        }
    }

    private void StartTimeoutMonitor(CancellationTokenSource testCts)
    {
        _ = Task.Run(async () =>
        {
            while (!testCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), testCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (testCts.Token.IsCancellationRequested || !IsTestingConnection)
                    break;

                bool shouldContinue = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    shouldContinue = StandardDialog.ShowConfirm(
                        "测试已超过 30 秒仍未完成，当前端点可能存在以下问题：\n\n" +
                        "• 端点地址不正确或不可达\n" +
                        "• 网络连接不稳定或延迟过高\n" +
                        "• 端点服务响应缓慢\n\n" +
                        "是否继续等待测试？",
                        "测试超时提醒");
                });

                if (!shouldContinue)
                {
                    try { testCts.Cancel(); }
                    catch { }
                    LogScoped("[ModelManagement] 用户取消超时测试");
                    break;
                }

                LogScoped("[ModelManagement] 用户选择继续等待测试");
            }
        });
    }

    private TreeNodeItem? FindProviderNodeInTree(string providerName)
    {
        foreach (var rootNode in TreeData)
        {
            var found = FindNodeByName(rootNode, providerName);
            if (found != null)
                return found;
        }
        return null;
    }

    private TreeNodeItem? FindNodeByName(TreeNodeItem node, string name)
    {
        if (node.Name == name)
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeByName(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    #region 内置分类访问密码验证

    private bool _paidPasswordVerified;
    private bool _publicPasswordVerified;

    private async Task<bool> VerifyBuiltInAccessPasswordAsync()
    {
        if (_currentEditingCategory == null) return true;

        var entryId = _currentEditingCategory.Id;
        var category = TM.Services.Framework.AI.Core.TianmingProviderIdentity.ResolveEntryCategory(entryId);

        if (category == null) return true;

        bool needsPassword = category == "paid"
            ? BuiltInConfigSyncService.PaidPasswordRequired
            : BuiltInConfigSyncService.PublicPasswordRequired;

        if (!needsPassword) return true;

        bool alreadyVerified = category == "paid" ? _paidPasswordVerified : _publicPasswordVerified;
        if (alreadyVerified) return true;

        var categoryName = category == "paid" ? "天命尊享" : "天命公益";
        var password = StandardDialog.ShowPasswordInput(
            $"【{categoryName}】已开启分类加密：\n请输入访问密码",
            $"{categoryName} - 访问验证");

        if (string.IsNullOrEmpty(password)) return false;

        try
        {
            var apiService = ServiceLocator.Get<ApiService>();
            var result = await apiService.VerifyAccessPasswordAsync(password, category);
            if (result.Success)
            {
                if (category == "paid") _paidPasswordVerified = true;
                else _publicPasswordVerified = true;
                GlobalToast.Success("验证通过", $"{categoryName} 访问密码验证成功");
                return true;
            }
            else
            {
                StandardDialog.ShowError(result.Message ?? "密码错误", "验证失败");
                return false;
            }
        }
        catch (Exception ex)
        {
            StandardDialog.ShowError($"验证请求失败: {ex.Message}", "网络错误");
            return false;
        }
    }

    #endregion
}
