using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ValidationResultViewModel : DataManagementViewModelBase<ValidationSummaryData, ValidationSummaryCategory, ValidationSummaryService>, TM.Framework.Common.ViewModels.IAIGeneratingState
    {
        private static readonly Regex VolumeCategoryRegex = new("^第(?<n>\\d+)卷", RegexOptions.Compiled);

        private readonly IUnifiedValidationService _validationService;
        private CancellationTokenSource? _validateCts;
        private readonly RelayCommand _cancelValidationCommand;

        public ValidationResultViewModel(IUnifiedValidationService validationService)
        {
            _validationService = validationService;
            _cancelValidationCommand = new RelayCommand(CancelValidation, () => _validateCts != null);
        }

        private IUnifiedValidationService ValidationService => _validationService;

        ICommand TM.Framework.Common.ViewModels.IAIGeneratingState.CancelBatchGenerationCommand => _cancelValidationCommand;

        #region 标准4字段

        private string _formName = string.Empty;
        private string _formIcon = "";
        private string _formStatus = "已启用";
        private string _formCategory = string.Empty;

        public string FormName
        {
            get => _formName;
            set { _formName = value; OnPropertyChanged(); }
        }

        public string FormIcon
        {
            get => _formIcon;
            set { _formIcon = value; OnPropertyChanged(); }
        }

        public string FormStatus
        {
            get => _formStatus;
            set { _formStatus = value; OnPropertyChanged(); }
        }

        public string FormCategory
        {
            get => _formCategory;
            set
            {
                if (_formCategory != value)
                {
                    _formCategory = value;
                    OnPropertyChanged();
                    OnCategoryValueChanged(_formCategory);
                }
            }
        }

        #endregion

        #region 选中数据和统计属性

        private ValidationSummaryData? _selectedData;

        public ValidationSummaryData? SelectedData
        {
            get => _selectedData;
            set
            {
                _selectedData = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PassedCount));
                OnPropertyChanged(nameof(WarningCount));
                OnPropertyChanged(nameof(FailedCount));
                OnPropertyChanged(nameof(NotValidatedCount));
                OnPropertyChanged(nameof(OverallResultIcon));
                UpdateProblemItems();
            }
        }

        public int PassedCount => SelectedData?.ModuleResults.Count(m => m.Result == "通过") ?? 0;

        public int WarningCount => SelectedData?.ModuleResults.Count(m => m.Result == "警告") ?? 0;

        public int FailedCount => SelectedData?.ModuleResults.Count(m => m.Result == "失败") ?? 0;

        public int NotValidatedCount => SelectedData?.ModuleResults.Count(m => m.Result == "未校验") ?? 0;

        public string OverallResultIcon => SelectedData?.OverallResult switch
        {
            "通过" => "✅",
            "警告" => "⚠️",
            "失败" => "❌",
            _ => "⏳"
        };

        #endregion

        #region 扁平问题清单

        public ObservableCollection<ProblemItemDisplay> ProblemItems { get; } = new();

        private void UpdateProblemItems()
        {
            ProblemItems.Clear();

            if (SelectedData == null)
                return;

            foreach (var module in SelectedData.ModuleResults
                .Where(m => m.Result is "警告" or "失败" or "未校验"))
            {
                try
                {
                    var items = string.IsNullOrEmpty(module.ProblemItemsJson)
                        ? new List<ProblemItem>()
                        : JsonSerializer.Deserialize<List<ProblemItem>>(module.ProblemItemsJson) ?? new();

                    foreach (var item in items)
                    {
                        ProblemItems.Add(new ProblemItemDisplay
                        {
                            ModuleName = module.DisplayName,
                            Summary = item.Summary,
                            Reason = item.Reason,
                            Details = item.Details,
                            Suggestion = item.Suggestion,
                            ChapterId = item.ChapterId,
                            ChapterTitle = item.ChapterTitle
                        });
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ValidationResultViewModel] 解析问题项失败: {module.ModuleName}, {ex.Message}");
                }
            }
        }

        #endregion

        protected override bool SupportsBatch(TreeNodeItem categoryNode) => false;

        protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
        {
            AIGenerateButtonText = "AI校验";
            IsAIGenerateEnabled = hasSelection && _currentEditingCategory != null;
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (_currentEditingCategory == null)
                return false;

            return TryParseVolumeNumber(_currentEditingCategory.Name, out _);
        }

        protected override async System.Threading.Tasks.Task ExecuteAIGenerateAsync()
        {
            var skChat = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
            if (skChat.IsMainConversationGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;
                skChat.CancelCurrentRequest();
            }

            if (_currentEditingCategory == null)
            {
                GlobalToast.Warning("无法校验", "请先双击选择要校验的卷分类（如第1卷）");
                return;
            }

            if (!TryParseVolumeNumber(_currentEditingCategory.Name, out var volumeNumber))
            {
                GlobalToast.Warning("无法校验", $"卷分类名称格式不正确：{_currentEditingCategory.Name}");
                return;
            }

            _validateCts?.Cancel();
            _validateCts?.Dispose();
            _validateCts = new CancellationTokenSource();
            var ct = _validateCts.Token;

            _cancelValidationCommand.RaiseCanExecuteChanged();

            try
            {
                TM.App.Log($"[ValidationResultViewModel] 开始AI校验：第{volumeNumber}卷");
                await ValidationService.ValidateVolumeAsync(volumeNumber, ct);

                if (ct.IsCancellationRequested)
                {
                    GlobalToast.Warning("已取消", $"第{volumeNumber}卷校验已取消");
                    return;
                }

                RefreshTreeAndCategorySelection();

                var latest = Service.GetDataByVolumeNumber(volumeNumber);
                if (latest != null)
                {
                    _currentEditingData = latest;
                    _currentEditingCategory = null;
                    SelectedData = latest;
                    LoadDataToForm(latest);
                    OnDataItemLoaded();
                }

                GlobalToast.Success("校验完成", $"第{volumeNumber}卷校验已完成");
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[ValidationResultViewModel] AI校验已取消: 第{volumeNumber}卷");
                GlobalToast.Warning("已取消", $"第{volumeNumber}卷校验已取消");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultViewModel] AI校验失败: 第{volumeNumber}卷, {ex}");
                GlobalToast.Error("校验失败", ex.Message);
            }
            finally
            {
                _validateCts?.Dispose();
                _validateCts = null;
                _cancelValidationCommand.RaiseCanExecuteChanged();
            }
        }

        private void CancelValidation()
        {
            try
            {
                _validateCts?.Cancel();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultViewModel] 取消校验失败: {ex.Message}");
            }
        }

        private static bool TryParseVolumeNumber(string? categoryName, out int volumeNumber)
        {
            volumeNumber = 0;
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            var m = VolumeCategoryRegex.Match(categoryName.Trim());
            if (!m.Success)
                return false;

            return int.TryParse(m.Groups["n"].Value, out volumeNumber) && volumeNumber > 0;
        }

        #region 抽象方法实现

        protected override string DefaultDataIcon => "✅";

        protected override ValidationSummaryData? CreateNewData(string? categoryName = null)
        {
            return null;
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName) => FormCategory = categoryName;

        protected override int ClearAllDataItems()
        {
            var allData = Service.GetAllData();
            var count = allData.Count;
            foreach (var data in allData)
            {
                Service.DeleteData(data.Id);
            }
            return count;
        }

        protected override List<ValidationSummaryCategory> GetAllCategoriesFromService()
        {
            return Service.GetAllCategories();
        }

        protected override List<ValidationSummaryData> GetAllDataItems()
        {
            return Service.GetAllData();
        }

        protected override string GetDataCategory(ValidationSummaryData data)
        {
            return data.Category;
        }

        protected override TreeNodeItem ConvertToTreeNode(ValidationSummaryData data)
        {
            var resultIcon = data.OverallResult switch
            {
                "通过" => "✅",
                "警告" => "⚠️",
                "失败" => "❌",
                _ => "⏳"
            };

            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = resultIcon,
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override bool MatchesSearchKeyword(ValidationSummaryData data, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return true;

            return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.TargetVolumeName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || data.OverallResult.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 节点双击命令

        private ICommand? _selectNodeCommand;

        public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: ValidationSummaryData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    SelectedData = data;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                    AIGenerateButtonText = "AI校验";
                    IsAIGenerateEnabled = false;
                }
                else if (param is TreeNodeItem { Tag: ValidationSummaryCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    SelectedData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                    AIGenerateButtonText = "AI校验";
                    IsAIGenerateEnabled = TryParseVolumeNumber(category.Name, out _);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        });

        private void LoadDataToForm(ValidationSummaryData data)
        {
            FormName = data.Name;
            FormIcon = data.Icon;
            FormStatus = "已启用";
            FormCategory = data.Category;
        }

        private void LoadCategoryToForm(ValidationSummaryCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = category.IsEnabled ? "已启用" : "已禁用";
            FormCategory = category.ParentCategory ?? string.Empty;
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = "📚";
            FormStatus = "已启用";
            FormCategory = string.Empty;
            SelectedData = null;
        }

        #endregion

        #region AddCommand

        private ICommand? _addCommand;

        public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），选中任意数据项保存即为全量保存");
        });

        #endregion

        #region SaveCommand

        private ICommand? _saveCommand;

        public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
        {
            GlobalToast.Info("提示", "卷分类来自分卷设计（只读），校验数据由AI自动生成");
        });

        #endregion

        #region DeleteCommand

        private ICommand? _deleteCommand;

        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                if (_currentEditingCategory != null)
                {
                    GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
                    return;
                }

                if (_currentEditingData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除校验数据『{_currentEditingData.Name}』吗？",
                        "确认删除");
                    if (!result)
                        return;

                    Service.DeleteData(_currentEditingData.Id);
                    GlobalToast.Success("删除成功", $"校验数据『{_currentEditingData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的数据");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", ex.Message);
            }
        });

        #endregion

        #region 定位章节命令

        private ICommand? _locateChapterCommand;

        public ICommand LocateChapterCommand => _locateChapterCommand ??= new RelayCommand(param =>
        {
            if (param is not ProblemItemDisplay item || string.IsNullOrWhiteSpace(item.ChapterId))
            {
                GlobalToast.Warning("无法定位", "该问题未关联章节信息");
                return;
            }
            try
            {
                var dialog = new ChapterRepairDialog(item.ChapterId, item.ChapterTitle ?? item.ChapterId, ProblemItems
                    .Where(p => p.ChapterId == item.ChapterId)
                    .ToList());
                dialog.Owner = System.Windows.Application.Current.MainWindow;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultViewModel] 打开修复弹窗失败: {ex.Message}");
                GlobalToast.Error("打开失败", ex.Message);
            }
        });

        #endregion

        #region 版本追踪支持

        protected override string GetModuleNameForVersionTracking() => "ValidationSummary";

        protected override void SaveCurrentEditingData()
        {
            if (_currentEditingData != null)
            {
                Service.UpdateData(_currentEditingData);
            }
        }

        #endregion
    }

    #region 辅助类

    public class ProblemItemDisplay
    {
        public string ModuleName { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string? Details { get; set; }

        public string? Suggestion { get; set; }

        public string? ChapterId { get; set; }

        public string? ChapterTitle { get; set; }

        public bool HasChapterLocation => !string.IsNullOrWhiteSpace(ChapterId);
    }

    #endregion
}
