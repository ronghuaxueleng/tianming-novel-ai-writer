using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ProjectSpecPanelViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public event Action? SaveCompleted;

        private readonly SpecLoader _specLoader;
        private readonly PromptService _promptService;

        private string? _writingStyle;
        private string? _pov;
        private string? _tone;
        private int _targetWordCount;
        private int _paragraphLength;
        private double _dialogueRatioPercent;
        private string _mustIncludeText = "";
        private string _mustAvoidText = "";
        private string _statusMessage = "";
        private bool _hasStatusMessage;
        private string? _selectedTemplateName;
        private int _polishMode = CreativeSpec.DefaultPolishMode;
        private int _polishModel = CreativeSpec.DefaultPolishModel;
        private int _wordCountControl = CreativeSpec.DefaultWordCountControl;
        private int _polishControl = CreativeSpec.DefaultPolishControl;
        private string? _pendingTemplateNameRestore;
        private bool _isSavingInternally;

        public ProjectSpecPanelViewModel(SpecLoader specLoader, PromptService promptService)
        {
            _specLoader = specLoader;
            _promptService = promptService;

            WritingStyleOptions.ReplaceAll(DefaultWritingStyleOptions);
            ToneOptions.ReplaceAll(DefaultToneOptions);

            SaveCommand = new AsyncRelayCommand(async () => await SaveAsync());
            ResetCommand = new RelayCommand(Reset);

            PromptService.TemplatesChanged += OnPromptTemplatesChanged;

            _ = InitializeTemplatesAsync();

            _specLoader.SpecSaved += OnSpecSavedExternally;

            StoragePathHelper.CurrentProjectChanged += OnCurrentProjectChanged;

            _ = LoadAsync();
        }

        public void Dispose()
        {
            PromptService.TemplatesChanged -= OnPromptTemplatesChanged;
            _specLoader.SpecSaved -= OnSpecSavedExternally;
            StoragePathHelper.CurrentProjectChanged -= OnCurrentProjectChanged;
            GC.SuppressFinalize(this);
        }

        private void OnCurrentProjectChanged(string oldProject, string newProject)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    _specLoader.InvalidateCache();
                    await InitializeTemplatesAsync();
                    await LoadAsync();
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectSpecPanel] 项目切换后重载Spec失败: {ex.Message}");
            }
        }

        private void OnSpecSavedExternally(object? sender, EventArgs e)
        {
            if (_isSavingInternally) return;
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    _specLoader.InvalidateCache();
                    await LoadAsync();
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectSpecPanel] 外部Spec变更刷新失败: {ex.Message}");
            }
        }

        private void OnPromptTemplatesChanged(object? sender, EventArgs e)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    LoadSpecTemplateNames();
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectSpecPanel] 刷新模板列表失败: {ex.Message}");
            }
        }

        private async Task InitializeTemplatesAsync()
        {
            try
            {
                await _promptService.InitializeAsync();
                LoadSpecTemplateNames();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectSpecPanel] 初始化模板列表失败: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public string? WritingStyle
        {
            get => _writingStyle;
            set { if (_writingStyle != value) { _writingStyle = value; OnPropertyChanged(); } }
        }

        public string? Pov
        {
            get => _pov;
            set { if (_pov != value) { _pov = value; OnPropertyChanged(); } }
        }

        public string? Tone
        {
            get => _tone;
            set { if (_tone != value) { _tone = value; OnPropertyChanged(); } }
        }

        public int TargetWordCount
        {
            get => _targetWordCount;
            set { if (_targetWordCount != value) { _targetWordCount = value; OnPropertyChanged(); } }
        }

        public int ParagraphLength
        {
            get => _paragraphLength;
            set { if (_paragraphLength != value) { _paragraphLength = value; OnPropertyChanged(); } }
        }

        public double DialogueRatioPercent
        {
            get => _dialogueRatioPercent;
            set { if (Math.Abs(_dialogueRatioPercent - value) > 0.01) { _dialogueRatioPercent = value; OnPropertyChanged(); } }
        }

        public string MustIncludeText
        {
            get => _mustIncludeText;
            set { if (_mustIncludeText != value) { _mustIncludeText = value; OnPropertyChanged(); } }
        }

        public string MustAvoidText
        {
            get => _mustAvoidText;
            set { if (_mustAvoidText != value) { _mustAvoidText = value; OnPropertyChanged(); } }
        }

        public int PolishMode
        {
            get => _polishMode;
            set { if (_polishMode != value) { _polishMode = value; OnPropertyChanged(); } }
        }

        public int PolishModel
        {
            get => _polishModel;
            set { if (_polishModel != value) { _polishModel = value; OnPropertyChanged(); } }
        }

        public int WordCountControl
        {
            get => _wordCountControl;
            set { if (_wordCountControl != value) { _wordCountControl = value; OnPropertyChanged(); } }
        }

        public int PolishControl
        {
            get => _polishControl;
            set { if (_polishControl != value) { _polishControl = value; OnPropertyChanged(); } }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        public bool HasStatusMessage
        {
            get => _hasStatusMessage;
            set { if (_hasStatusMessage != value) { _hasStatusMessage = value; OnPropertyChanged(); } }
        }

        public RangeObservableCollection<string> TemplateNames { get; private set; } = new();

        public RangeObservableCollection<string> WritingStyleOptions { get; } = new RangeObservableCollection<string>();

        public RangeObservableCollection<string> ToneOptions { get; } = new RangeObservableCollection<string>();

        private static readonly string[] DefaultWritingStyleOptions =
        {
            "流畅自然", "热血激昂", "飘逸洒脱", "沉稳内敛", "爽快明快",
            "细腻温柔", "典雅古韵", "瑰丽奇幻", "豪迈洒脱", "理性冷峻",
            "严谨缜密", "厚重深沉", "铁血硬朗", "诡异惊悚", "轻松幽默",
            "朴实真挚", "精炼紧凑"
        };

        private static readonly string[] DefaultToneOptions =
        {
            "平衡", "紧张悬疑", "温馨治愈", "冷峻客观", "轻松诙谐",
            "平和深沉", "热血激昂", "侠义豪情", "史诗宏大", "缠绵悱恻",
            "严肃深沉", "豪迈悲壮"
        };

        public string? SelectedTemplateName
        {
            get => _selectedTemplateName;
            set
            {
                if (_selectedTemplateName != value)
                {
                    if (value == null && !string.IsNullOrWhiteSpace(_pendingTemplateNameRestore))
                    {
                        return;
                    }

                    _selectedTemplateName = value;
                    _pendingTemplateNameRestore = null;
                    OnPropertyChanged();
                    if (!string.IsNullOrEmpty(value))
                    {
                        ApplyTemplate();
                    }
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        #endregion

        #region 方法

        private void LoadSpecTemplateNames()
        {
            var names = new System.Collections.Generic.List<string>();

            if (string.IsNullOrWhiteSpace(_pendingTemplateNameRestore)
                && !string.IsNullOrWhiteSpace(_selectedTemplateName))
            {
                _pendingTemplateNameRestore = _selectedTemplateName;
            }

            var allCategories = _promptService.GetAllCategories();
            var specLv2Categories = allCategories
                .Where(c => c.Level == 2 && c.ParentCategory == "Spec")
                .ToList();

            foreach (var category in specLv2Categories)
            {
                var templates = _promptService.GetTemplatesByCategory(category.Name);
                names.AddRange(templates.Select(t => t.Name));
            }

            TemplateNames ??= new RangeObservableCollection<string>();
            ReplaceCollection(TemplateNames, names.Where(n => !string.IsNullOrWhiteSpace(n)));

            TryRestoreTemplateNameAfterTemplatesLoaded();
        }

        private static void ReplaceCollection<T>(RangeObservableCollection<T> target, IEnumerable<T> items)
        {
            target.ReplaceAll(items is IList<T> list ? list : items.ToList());
        }

        private void TryRestoreTemplateNameAfterTemplatesLoaded()
        {
            if (string.IsNullOrWhiteSpace(_pendingTemplateNameRestore))
            {
                return;
            }

            var pending = _pendingTemplateNameRestore;

            if (TemplateNames == null || TemplateNames.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(pending) && TemplateNames.Contains(pending))
            {
                _selectedTemplateName = pending;
                OnPropertyChanged(nameof(SelectedTemplateName));

                _pendingTemplateNameRestore = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_selectedTemplateName)
                && string.Equals(_selectedTemplateName, pending, StringComparison.Ordinal))
            {
                _selectedTemplateName = null;
                OnPropertyChanged(nameof(SelectedTemplateName));
            }

            _pendingTemplateNameRestore = null;
        }

        private async Task LoadAsync()
        {
            try
            {
                var spec = await _specLoader.LoadProjectSpecAsync();
                if (spec != null)
                {
                    ApplySpec(spec, restoreTemplateName: true);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProjectSpecPanel] 加载失败: {ex.Message}");
            }
        }

        private async Task SaveAsync()
        {
            _isSavingInternally = true;
            try
            {
                if (PolishMode == 2)
                {
                    var confirmed = StandardDialog.ShowConfirm(
                        "二次润色会大幅增加生成时间（约一倍），确定开启吗？",
                        "二次润色");
                    if (!confirmed)
                    {
                        PolishMode = 0;
                        return;
                    }
                }

                var spec = BuildSpec();
                await _specLoader.SaveProjectSpecAsync(spec);
                _specLoader.InvalidateCache();

                StatusMessage = !string.IsNullOrWhiteSpace(SelectedTemplateName)
                    ? $"已应用模板：{SelectedTemplateName}"
                    : "✓ 保存成功";
                HasStatusMessage = true;
                TM.App.Log("[ProjectSpecPanel] 保存项目Spec成功");

                await Task.Delay(500);
                SaveCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
                HasStatusMessage = true;
                TM.App.Log($"[ProjectSpecPanel] 保存失败: {ex.Message}");
            }
            finally
            {
                _isSavingInternally = false;
            }
        }

        private void Reset()
        {
            WritingStyle = "流畅自然";
            Pov = "第三人称限知";
            Tone = "平衡";
            TargetWordCount = 3500;
            ParagraphLength = 200;
            DialogueRatioPercent = 30;
            MustIncludeText = "";
            MustAvoidText = "";
            PolishMode = CreativeSpec.DefaultPolishMode;
            PolishModel = CreativeSpec.DefaultPolishModel;
            WordCountControl = CreativeSpec.DefaultWordCountControl;
            PolishControl = CreativeSpec.DefaultPolishControl;
            _pendingTemplateNameRestore = null;
            SelectedTemplateName = null;
            WritingStyleOptions.ReplaceAll(DefaultWritingStyleOptions);
            ToneOptions.ReplaceAll(DefaultToneOptions);

            StatusMessage = "已重置为默认值";
            HasStatusMessage = true;
            TM.App.Log("[ProjectSpecPanel] 重置为默认值");
        }

        private void ApplyTemplate()
        {
            if (string.IsNullOrEmpty(SelectedTemplateName))
                return;

            TM.App.Log($"[ProjectSpecPanel] 尝试应用模板: {SelectedTemplateName}");

            var allTemplates = _promptService.GetAllTemplates();
            TM.App.Log($"[ProjectSpecPanel] 模板总数: {allTemplates.Count}");

            var promptTemplate = allTemplates.FirstOrDefault(t => t.Name == SelectedTemplateName);

            if (promptTemplate != null)
            {
                TM.App.Log($"[ProjectSpecPanel] 找到模板，SystemPrompt长度: {promptTemplate.SystemPrompt?.Length ?? 0}");
                var spec = SpecTemplateParser.Parse(promptTemplate.SystemPrompt ?? "", SelectedTemplateName ?? "");
                TM.App.Log($"[ProjectSpecPanel] 解析结果: 风格={spec.WritingStyle}, 视角={spec.Pov}, 基调={spec.Tone}");
                UpdateStyleAndToneOptions(promptTemplate.SystemPrompt ?? "", spec);
                ApplySpec(spec);
                StatusMessage = $"当前选择模板：{SelectedTemplateName}";
                HasStatusMessage = true;
                TM.App.Log($"[ProjectSpecPanel] 应用模板成功: {SelectedTemplateName}");
            }
            else
            {
                TM.App.Log($"[ProjectSpecPanel] 未找到模板: {SelectedTemplateName}");
                var names = string.Join(", ", allTemplates.Select(t => t.Name).Take(10));
                TM.App.Log($"[ProjectSpecPanel] 可用模板: {names}");
            }
        }

        private void UpdateStyleAndToneOptions(string systemPrompt, CreativeSpec spec)
        {
            var styleList = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(spec.WritingStyle)) styleList.Add(spec.WritingStyle);
            foreach (var s in DefaultWritingStyleOptions)
                if (!styleList.Contains(s)) styleList.Add(s);
            WritingStyleOptions.ReplaceAll(styleList);

            var toneList = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(spec.Tone)) toneList.Add(spec.Tone);
            foreach (var t in DefaultToneOptions)
                if (!toneList.Contains(t)) toneList.Add(t);
            ToneOptions.ReplaceAll(toneList);
        }

        private void ApplySpec(CreativeSpec spec, bool restoreTemplateName = false)
        {
            WritingStyle = spec.WritingStyle ?? "流畅自然";
            Pov = spec.Pov ?? "第三人称限知";
            Tone = spec.Tone ?? "平衡";
            TargetWordCount = spec.TargetWordCount ?? 3500;
            ParagraphLength = spec.ParagraphLength ?? 200;
            DialogueRatioPercent = (spec.DialogueRatio ?? 0.3) * 100;
            MustIncludeText = spec.MustInclude != null ? string.Join("、", spec.MustInclude) : "";
            MustAvoidText = spec.MustAvoid != null ? string.Join("、", spec.MustAvoid) : "";
            PolishMode = CreativeSpec.GetEffectivePolishMode(spec);
            PolishModel = CreativeSpec.GetEffectivePolishModel(spec);
            WordCountControl = CreativeSpec.GetEffectiveWordCountControl(spec);
            PolishControl = CreativeSpec.GetEffectivePolishControl(spec);

            if (restoreTemplateName && !string.IsNullOrEmpty(spec.TemplateName))
            {
                _pendingTemplateNameRestore = spec.TemplateName;
                _selectedTemplateName = spec.TemplateName;
                OnPropertyChanged(nameof(SelectedTemplateName));
            }
        }

        private CreativeSpec BuildSpec()
        {
            return new CreativeSpec
            {
                TemplateName = SelectedTemplateName,
                WritingStyle = WritingStyle,
                Pov = Pov,
                Tone = Tone,
                TargetWordCount = TargetWordCount,
                ParagraphLength = ParagraphLength,
                DialogueRatio = DialogueRatioPercent / 100.0,
                MustInclude = ParseArrayText(MustIncludeText),
                MustAvoid = ParseArrayText(MustAvoidText),
                PolishMode = PolishMode,
                PolishModel = PolishModel,
                WordCountControl = WordCountControl,
                PolishControl = PolishControl
            };
        }

        private string[]? ParseArrayText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var items = text
                .Split(new[] { ',', '，', '、', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            return items.Length > 0 ? items : null;
        }

        #endregion
    }
}
