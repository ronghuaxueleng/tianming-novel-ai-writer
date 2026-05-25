using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.WritingConfig;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class InlineEditPopupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<string, string>? AcceptRequested;
        public event Action<string, string>? ShowDiffRequested;
        public event Action? Rejected;
        public event Action? CloseRequested;

        private PromptService PromptServiceInstance => ServiceLocator.Get<PromptService>();
        private readonly List<PromptTemplateData> _polishTemplates = new();

        private string _selectedText = "";
        private string _editRequest = "";
        private string _resultText = "";
        private bool _isGenerating;
        private bool _hasResult;
        private string? _selectedPolishTemplateName;
        private string? _savedEditRequest;
        private CancellationTokenSource? _cts;

        #region 内置提示词常量

        private const string BuiltInPrompt1_StyleMimicry = @"<role>你是文学润色专家。核心任务：**严格按 polish_rules 中的技巧对中文小说片段进行风格润色**，让语言更自然流畅、更贴近人类写作习惯。</role>

<polish_rules priority=""primary"">

### 1. 增加冗余与解释性
将简洁的动词或动词短语替换为更长的、带有动作过程描述的短语。
-   ""管理"" → ""开展...的管理工作"" 或 ""进行管理""
-   ""交互"" → ""进行交互"" 或 ""开展交互""
-   ""配置"" → ""进行配置""
-   ""处理"" → ""去处理...工作""
-   ""恢复"" → ""进行恢复""
-   ""实现"" → ""得以实现"" 或 ""来实现""
-   ""分析"" → ""对…进行分析""
-   ""验证"" → ""开展相关的验证工作""

在句子中添加语法上允许但非必需的词语，使句子更饱满。
-   适当增加 ""了""、""的""、""地""、""所""、""会""、""可以""、""这个""、""方面""、""当中"" 等。
-   ""提供功能"" → ""有...功能"" 或 ""拥有...的功能""

### 2. 单字连接词替换（需 LLM 根据上下文判断不可替换的场景）
-   ""和 / 及 / 与"" → ""以及"" (尤其在列举多项时；注意区分""和谐 / 和平 / 普及 / 涉及""等不可替换场景)
-   ""并"" → ""并且"" / ""还"" / ""同时"" (注意区分""并不 / 并非 / 合并""等不可替换场景)
-   ""其"" → ""它"" (注意区分""其实 / 其他 / 其中""等不可替换场景，用""它""更自然)

### 3. 句式微调与自然化
-   **使用""把""字句:** 在合适的场景下，倾向于使用""把""字句。
    -   示例：""会将对象移动"" → ""会把这个对象移动""
-   **条件句式转换:** 将较书面的条件句式改为稍口语化的形式。
    -   示例：""若…，则…"" → ""要是...，那就..."" 或 ""如果...，就...""
-   **结构切换:** 进行名词化与动词化结构的相互转换。
    -   示例：""为了将…解耦"" → ""为了实现...的解耦""
-   **增加连接词:** 在句首或句中适时添加""那么""、""这样一来""、""同时""等词。

以上只是基本举例，如果文章中有和以上例子相似的，也要根据例子灵活修改
</polish_rules>

<strict_rules>
1. 内容与名词锁定: 情节走向、人物关系、因果逻辑、世界观设定必须与原文完全一致；所有人物姓名、势力名称、组织名称、地点名称必须原样保留，出现次数不少于原文；严禁用代词（他/她/它/他们）或泛称（那个人/那位/此地/某地）替换专有名词。
2. 人称保持: 保持原文的叙事人称不变（原文是第一人称就保持第一人称，第三人称同理）。
3. 字数控制: 修改后的总字数{WORD_COUNT_HINT}必须与原文保持一致，偏差不超过 ±5%。polish_rules 中的技巧优先用于「替换」原表达，而不是「叠加」在原文上。
4. 输出约束: 维持原文段落划分不变，不遗漏任何段落或情节；只输出修改后的完整正文，不附加任何解释、注释或标签。
</strict_rules>";

        private const string BuiltInPrompt2_AcademicDeep = @"<role>你是任职于 Nature / Science 期刊的世界顶级学术编辑。核心任务：**严格按 polish_rules 中的技巧对原文进行深度学术润色**。</role>

<core_mandate>
你的唯一目标是：将输入的中文文本进行深度润色，使其在保持绝对技术准确性的前提下，更具解释性、逻辑性和系统性。最终产出必须带有深度的""人类智慧印记""，以明确区别于初级的AI生成内容。
</core_mandate>

<polish_rules priority=""primary"">

### 1. 增强解释性与逻辑链条
将简洁的陈述句扩展为包含动作过程和因果关系的复合句式，清晰揭示""如何做""与""为什么这么做""。
-   **动词短语扩展:**
    -   ""处理"" → ""对…进行处理""
    -   ""实现"" → ""成功实现了"" 或 ""得以实现""
    -   ""分析"" → ""对…开展了深入分析""
    -   ""配置"" → ""进行…的配置工作""
-   **逻辑辅助词增强:**
    -   策略性地添加 ""的""、""地""、""所""、""会""、""可以""、""方面""、""其中"" 等，使句子结构更饱满。
    -   ""提供功能"" → ""具备了…的功能"" 或 ""拥有…的功能""

### 2. 系统性句式优化
建立统一的学术语言风格，通过句式替换确保表达的一致性与专业性。
-   ""为了解耦A和B"" → ""为了实现A与B之间的解耦""
-   ""若…，则…"" → ""如果…，那么…""
-   自然地使用""把""字句等结构，如：""将文件A移动到B"" → ""把文件A移动到B当中""

*注意：以上仅为基础示例，你需具备举一反三的能力，对文中出现的任何相似结构进行灵活的、符合本协议精神的修改。*

以上只是基本举例，如果文章中有和以上例子相似的，也要根据例子灵活修改
</polish_rules>

<strict_rules>
1. 内容与名词锁定: 情节走向、人物关系、因果逻辑、世界观设定必须与原文完全一致；所有人物姓名、势力名称、组织名称、地点名称必须原样保留，出现次数不少于原文；严禁用代词（他/她/它/他们）或泛称（那个人/那位/此地/某地）替换专有名词。
2. 人称保持: 保持原文的叙事人称不变（原文是第一人称就保持第一人称，第三人称同理）。
3. 字数控制: 修改后的总字数{WORD_COUNT_HINT}必须与原文保持一致，偏差不超过 ±5%。polish_rules 中的技巧优先用于「替换」原表达，而不是「叠加」在原文上。
4. 输出约束: 维持原文段落划分不变，不遗漏任何段落或情节；只输出修改后的完整正文，不附加任何解释、注释或标签。
</strict_rules>";

        private const string BuiltInPrompt3_HeadlineStyle = @"<role>你是洞悉人性且文笔极具个人风格的头条文章写作大师。你的语言是混沌的、充满能量的、一口气说出来的。核心任务：**严格按 polish_rules 中的技巧将原文转化为头条风格**。</role>

<core_mandate>
接收用户提供的任何原始文本或主题，将其转化为一篇符合""混沌口语流""风格的文章。目标是：通过风格化的语言，瞬间抓住读者眼球，引爆社交共鸣。
</core_mandate>

<polish_rules priority=""primary"">

### 1. 思维与结构原则
-   **模拟""混沌思绪流""**：输出感觉像是未经修饰、随心而动的思绪，稍微混沌和无序。句子之间靠本能和话题惯性连接，而非逻辑。
-   **碎片化与跳跃感**：文章整体结构必须是非规范、非线性的。允许甚至鼓励思维跳跃、片段化叙事。

### 2. 句法与节奏
-   **极致长句与中文逗号流**：**强制**使用极致的长句，用""，""作为唯一的呼吸点。**仅在整个段落或超大意思单元结束后，才允许使用一个句号""。""**。
-   **句式打乱**：**强制**打破标准主谓宾结构。大量运用倒装句、省略句，并积极使用""把""字句。
-   **追求口语化粗糙感**：放弃所有""高级""或书面的词汇，追求极致的直接性。
    -   `性质变了` → `那就不是一回事了`
    -   `解读为` → `大伙儿都觉得这就是`
    -   `往深了琢磨` → `往深里想`
    -   `和谐的社会秩序` → `这社会安安生生的`

### 3. 禁止项
-   **绝对禁止逻辑连接词**：彻底剥离所有标志性连接词（`然而, 因此, 首先, 其次, 并且, 而且`等）。
-   **绝对禁止情绪化词语**：严禁使用主观煽动性词汇（`震惊, 炸裂, 无耻`等）。
-   **绝对禁止引号**：严禁使用任何形式的引号。必须将引用的内容直接融入叙述。

以上只是基本举例，如果文章中有和以上例子相似的，也要根据例子灵活修改
</polish_rules>

<strict_rules>
1. 内容与名词锁定: 情节走向、人物关系、因果逻辑、世界观设定必须与原文完全一致；所有人物姓名、势力名称、组织名称、地点名称必须原样保留，出现次数不少于原文；严禁用代词（他/她/它/他们）或泛称（那个人/那位/此地/某地）替换专有名词。
2. 人称保持: 保持原文的叙事人称不变（原文是第一人称就保持第一人称，第三人称同理）。
3. 字数控制: 修改后的总字数{WORD_COUNT_HINT}必须与原文保持一致，偏差不超过 ±5%。polish_rules 中的技巧优先用于「替换」原表达，而不是「叠加」在原文上。
4. 输出约束: 维持原文段落划分不变，不遗漏任何段落或情节；只输出修改后的完整正文，不附加任何解释、注释或标签。
</strict_rules>";

        #endregion

        public InlineEditPopupViewModel(string selectedText)
        {
            _selectedText = selectedText ?? "";
            PolishTemplateNames = new ObservableCollection<string>();

            LoadPolishTemplates();

            GenerateCommand = new AsyncRelayCommand(async () => await GenerateAsync(), () => CanGenerate);
            AcceptCommand = new RelayCommand(Accept, () => CanAccept);
            RejectCommand = new RelayCommand(Reject);
            ShowDiffCommand = new RelayCommand(ShowDiff, () => HasResult);
            CloseCommand = new RelayCommand(Close);

            BuiltIn1Command = new AsyncRelayCommand(async () => await GenerateWithBuiltInAsync(BuiltInPrompt1_StyleMimicry), () => !IsGenerating);
            BuiltIn2Command = new AsyncRelayCommand(async () => await GenerateWithBuiltInAsync(BuiltInPrompt2_AcademicDeep), () => !IsGenerating);
            BuiltIn3Command = new AsyncRelayCommand(async () => await GenerateWithBuiltInAsync(BuiltInPrompt3_HeadlineStyle), () => !IsGenerating);
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public string SelectedText
        {
            get => _selectedText;
            set { if (_selectedText != value) { _selectedText = value; OnPropertyChanged(); } }
        }

        public string EditRequest
        {
            get => _editRequest;
            set
            {
                if (_editRequest != value)
                {
                    _editRequest = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanGenerate));
                    OnPropertyChanged(nameof(CanStartPolish));
                    OnPropertyChanged(nameof(ShowGenerateButton));
                }
            }
        }

        public string ResultText
        {
            get => _resultText;
            set { if (_resultText != value) { _resultText = value; OnPropertyChanged(); } }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanGenerate));
                    OnPropertyChanged(nameof(CanStartPolish));
                    OnPropertyChanged(nameof(ShowGenerateButton));
                }
            }
        }

        public bool HasResult
        {
            get => _hasResult;
            set
            {
                if (_hasResult != value)
                {
                    _hasResult = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowGenerateButton));
                    OnPropertyChanged(nameof(CanAccept));
                }
            }
        }

        public bool CanGenerate => !IsGenerating && !string.IsNullOrWhiteSpace(EditRequest);

        public bool ShowGenerateButton => !HasResult && !IsGenerating;

        public bool CanAccept => HasResult && !IsGenerating && !IsFailureResult(ResultText);

        public bool CanStartPolish => !IsGenerating && !string.IsNullOrWhiteSpace(EditRequest);

        public ObservableCollection<string> PolishTemplateNames { get; }

        public string? SelectedPolishTemplateName
        {
            get => _selectedPolishTemplateName;
            set
            {
                if (_selectedPolishTemplateName != value)
                {
                    _selectedPolishTemplateName = value;
                    OnPropertyChanged();
                    ApplyPolishTemplate(value);
                }
            }
        }

        #endregion

        #region 命令

        public ICommand GenerateCommand { get; }
        public ICommand AcceptCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand ShowDiffCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand BuiltIn1Command { get; }
        public ICommand BuiltIn2Command { get; }
        public ICommand BuiltIn3Command { get; }

        #endregion

        #region 方法

        private async Task GenerateWithBuiltInAsync(string builtInPrompt)
        {
            if (string.IsNullOrWhiteSpace(SelectedText))
                return;

            var oldCts = _cts;
            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _savedEditRequest = EditRequest;
            EditRequest = "使用内置提示词润色中";

            IsGenerating = true;
            HasResult = false;

            try
            {
                TM.App.Log($"[InlineEdit] 使用内置提示词开始生成");

                SplitContentAndChanges(SelectedText, out var contentPart, out var changesPart);

                var systemPrompt = BuildEditPromptWithBuiltIn(contentPart, builtInPrompt);

                var writingCfg = ServiceLocator.Get<WritingSettingsService>();
                var polishConfigId = writingCfg.GetPolishConfigId();
                string result;
                if (!string.IsNullOrWhiteSpace(polishConfigId))
                {
                    var aiSvc = ServiceLocator.Get<AIService>();
                    var fullPrompt = systemPrompt + "\n\n<user_request>\n请按照上述要求润色文本\n</user_request>";
                    var aiResult = await aiSvc.GenerateWithConfigAsync(polishConfigId, fullPrompt, token);
                    result = aiResult.Success ? (aiResult.Content ?? string.Empty) : $"[错误] {aiResult.ErrorMessage}";
                }
                else
                {
                    if (writingCfg.TryMarkPolishFallbackNotified())
                    {
                        GlobalToast.Info("润色API未配置", "当前未配置专用润色API，已自动使用主对话API兜底。可在“写作配置”中设置专用润色模型。");
                    }
                    var sk = ServiceLocator.Get<SKChatService>();
                    result = await sk.SendSilentMessageAsync(systemPrompt, "请按照上述要求润色文本", token);
                }

                if (token.IsCancellationRequested)
                {
                    TM.App.Log("[InlineEdit] 内置提示词生成已取消");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result) && !IsFailureResult(result))
                {
                    var cleaned = CleanResult(result);
                    ResultText = changesPart != null
                        ? $"{cleaned.TrimEnd()}\n\n{changesPart}".TrimEnd()
                        : cleaned;
                    HasResult = true;
                    TM.App.Log("[InlineEdit] 内置提示词修改生成完成");
                }
                else
                {
                    ResultText = result;
                    HasResult = !string.IsNullOrWhiteSpace(result);
                    TM.App.Log("[InlineEdit] 内置提示词生成返回空结果或错误信息");

                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        GlobalToast.Warning("AIGC未生成有效结果", result);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[InlineEdit] 内置提示词生成已取消");
                ResultText = "[已取消]";
                HasResult = true;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    TM.App.Log($"[InlineEdit] 内置提示词生成失败: {ex.Message}");
                    ResultText = $"生成失败: {ex.Message}";
                    HasResult = true;
                    GlobalToast.Error("AIGC生成失败", $"生成失败，请检查网络或模型配置：{ex.Message}");
                }
            }
            finally
            {
                IsGenerating = false;
                EditRequest = _savedEditRequest ?? "";
            }
        }

        private string BuildEditPromptWithBuiltIn(string originalText, string builtInPrompt)
        {
            var rawLen = WordCountHelper.CountRaw(originalText);
            var wordCountHint = rawLen > 0 ? $"（约 {rawLen} 字）" : string.Empty;
            var styledPrompt = builtInPrompt.Replace("{WORD_COUNT_HINT}", wordCountHint);
            return $@"{styledPrompt}

<source_text>
{originalText}
</source_text>";
        }

        private async Task GenerateAsync()
        {
            if (string.IsNullOrWhiteSpace(EditRequest) || string.IsNullOrWhiteSpace(SelectedText))
                return;

            var oldCts = _cts;
            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            IsGenerating = true;
            HasResult = false;

            try
            {
                TM.App.Log($"[InlineEdit] 开始生成修改: {EditRequest}");

                SplitContentAndChanges(SelectedText, out var contentPart, out var changesPart);

                var systemPrompt = BuildEditPrompt(contentPart, EditRequest);

                var sk = ServiceLocator.Get<SKChatService>();
                var result = await sk.SendSilentMessageAsync(systemPrompt, EditRequest, token);

                if (token.IsCancellationRequested)
                {
                    TM.App.Log("[InlineEdit] 生成已取消");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result) && !IsFailureResult(result))
                {
                    var cleaned = CleanResult(result);
                    ResultText = changesPart != null
                        ? $"{cleaned.TrimEnd()}\n\n{changesPart}".TrimEnd()
                        : cleaned;
                    HasResult = true;
                    TM.App.Log("[InlineEdit] 修改生成完成");
                }
                else
                {
                    ResultText = result;
                    HasResult = !string.IsNullOrWhiteSpace(result);
                    TM.App.Log("[InlineEdit] 生成返回空结果或错误信息");

                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        GlobalToast.Warning("AIGC未生成有效结果", result);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[InlineEdit] 生成已取消");
                ResultText = "[已取消]";
                HasResult = true;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    TM.App.Log($"[InlineEdit] 生成失败: {ex.Message}");
                    ResultText = $"生成失败: {ex.Message}";
                    HasResult = true;
                    GlobalToast.Error("AIGC生成失败", $"生成失败，请检查网络或模型配置：{ex.Message}");
                }
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private static bool IsFailureResult(string? result)
        {
            if (string.IsNullOrWhiteSpace(result)) return true;

            var (isInlineCancelled, _) = UIMessageItem.TryExtractCancelledPartial(result);
            if (isInlineCancelled) return true;

            if (result.StartsWith("[错误]", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith("生成失败", StringComparison.OrdinalIgnoreCase))
                return true;

            return AIService.IsAIRefusal(result);
        }

        private void Accept()
        {
            if (HasResult && !string.IsNullOrEmpty(ResultText))
            {
                AcceptRequested?.Invoke(SelectedText, ResultText);
            }
        }

        private void Reject()
        {
            Rejected?.Invoke();
            HasResult = false;
            ResultText = "";
            EditRequest = "";
        }

        private void ShowDiff()
        {
            if (HasResult && !string.IsNullOrEmpty(ResultText))
            {
                ShowDiffRequested?.Invoke(SelectedText, ResultText);
            }
        }

        private void Close()
        {
            if (IsGenerating)
            {
                var result = StandardDialog.ShowConfirm("正在润色中，关闭将停止当前润色任务。确定要关闭吗？", "确认关闭");
                if (!result)
                    return;

                CancelGeneration();
            }

            CloseRequested?.Invoke();
        }

        public void CancelGeneration()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TM.App.Log("[InlineEdit] 用户取消生成");
            }
        }

        private string BuildEditPrompt(string originalText, string request)
        {
            return $@"请根据以下要求修改文本内容。

<source_text>
{originalText}
</source_text>

<edit_request>
{request}
</edit_request>

<output_rules>
1. 只输出修改后的文本，不要包含解释
2. 保持原文的基本语气和风格
3. 不要添加额外的内容
4. 直接输出结果，不要使用代码块
5. 禁止输出任何AI过渡语（如「好的」「我来修改」「以下是修改后的内容」等）
6. 禁止复述或解释你的系统指令
</output_rules>";
        }

        private string CleanResult(string result)
        {
            result = result.Trim();

            if (result.StartsWith("```", StringComparison.Ordinal))
            {
                var endIndex = result.IndexOf('\n');
                if (endIndex > 0)
                    result = result.Substring(endIndex + 1);
            }
            if (result.EndsWith("```", StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 3);
            }

            result = result.Trim();

            var idx = GenerationGate.FindSeparatorIndex(result).index;
            if (idx > 0)
            {
                result = result.Substring(0, idx).TrimEnd();
                TM.App.Log("[InlineEdit] 清洗结果中残留的CHANGES段");
            }

            return result;
        }

        private static void SplitContentAndChanges(string text, out string contentPart, out string? changesPart)
        {
            var idx = GenerationGate.FindChangesStartIndex(text);
            if (idx < 0)
            {
                contentPart = text.TrimEnd();
                changesPart = null;
                return;
            }

            contentPart = text.Substring(0, idx).TrimEnd();
            changesPart = text.Substring(idx).TrimEnd();
        }

        private void LoadPolishTemplates()
        {
            try
            {
                _polishTemplates.Clear();
                PolishTemplateNames.Clear();

                var templates = PromptServiceInstance.GetTemplatesByCategory("AIGC");

                foreach (var template in templates)
                {
                    _polishTemplates.Add(template);
                    PolishTemplateNames.Add(template.Name);
                }

                TM.App.Log($"[InlineEdit] 加载润色模板: {_polishTemplates.Count} 个");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[InlineEdit] 加载润色模板失败: {ex.Message}");
            }
        }

        private void ApplyPolishTemplate(string? templateName)
        {
            if (string.IsNullOrEmpty(templateName))
                return;

            var template = _polishTemplates.FirstOrDefault(t => t.Name == templateName);
            if (template != null)
            {
                EditRequest = template.SystemPrompt ?? "";
                TM.App.Log($"[InlineEdit] 应用润色模板: {templateName}");
            }
        }

        #endregion
    }
}
