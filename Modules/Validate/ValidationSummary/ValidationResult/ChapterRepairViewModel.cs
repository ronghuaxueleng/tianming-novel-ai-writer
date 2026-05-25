using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.UI.Workspace.CenterPanel.Controls;
using System.Reflection;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true)]
    public enum RepairDialogState { Reading, Generating, Comparing }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ChapterRepairViewModel : INotifyPropertyChanged
    {
        private readonly string _chapterId;
        private readonly Guid _repairSessionId = Guid.NewGuid();
        private readonly IGeneratedContentService _contentService;
        private CancellationTokenSource? _repairCts;

        public ChapterRepairViewModel(string chapterId, string chapterTitle, List<ProblemItemDisplay> problems)
        {
            _chapterId = chapterId;
            ChapterTitle = chapterTitle;
            Problems = problems;
            _contentService = ServiceLocator.Get<IGeneratedContentService>();

            RepairCommand = new RelayCommand(_ => StartRepairAsync().SafeFireAndForget(ex => TM.App.Log($"[ChapterRepairViewModel] {ex.Message}")), () => State == RepairDialogState.Reading);
            ConfirmCommand = new RelayCommand(_ => ConfirmSaveAsync().SafeFireAndForget(ex => TM.App.Log($"[ChapterRepairViewModel] {ex.Message}")), () => State == RepairDialogState.Comparing);
            CancelRepairCommand = new RelayCommand(_ => CancelRepair(), () => State == RepairDialogState.Generating && _repairCts != null);
        }

        #region 属性

        public string ChapterTitle { get; }
        public List<ProblemItemDisplay> Problems { get; }

        private string _chapterContent = string.Empty;
        public string ChapterContent
        {
            get => _chapterContent;
            set { _chapterContent = value; OnPropertyChanged(); }
        }

        private string _repairedContent = string.Empty;
        public string RepairedContent
        {
            get => _repairedContent;
            set { _repairedContent = value; OnPropertyChanged(); }
        }

        public DiffViewerViewModel DiffViewModel { get; } = new DiffViewerViewModel();

        private RepairDialogState _state = RepairDialogState.Reading;
        public RepairDialogState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsReading));
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(IsComparing));
                (RepairCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConfirmCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CancelRepairCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsReading => State == RepairDialogState.Reading;
        public bool IsGenerating => State == RepairDialogState.Generating;
        public bool IsComparing => State == RepairDialogState.Comparing;

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        private string _consistencyHint = string.Empty;
        public string ConsistencyHint
        {
            get => _consistencyHint;
            set { _consistencyHint = value; OnPropertyChanged(); }
        }

        public bool HasConsistencyHint => !string.IsNullOrWhiteSpace(ConsistencyHint);

        #endregion

        #region 命令

        public ICommand RepairCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelRepairCommand { get; }

        public event Action? CloseRequested;

        #endregion

        #region 初始化

        public async Task InitializeAsync()
        {
            try
            {
                ChapterContent = await _contentService.GetChapterAsync(_chapterId) ?? string.Empty;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterRepairViewModel] 加载章节内容失败: {ex.Message}");
                ChapterContent = $"（章节内容加载失败：{ex.Message}）";
            }
        }

        #endregion

        #region 修复逻辑

        private async Task StartRepairAsync()
        {
            var skChat = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
            if (skChat.IsWorkspaceBatchGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "工作台批量任务正在生成，继续需要中断该批量任务，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;
                skChat.CancelWorkspaceBatch();
            }

            if (skChat.IsMainConversationGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;
                skChat.CancelCurrentRequest();
            }

            _repairCts = new CancellationTokenSource();
            State = RepairDialogState.Generating;

            try
            {
                var repairService = ServiceLocator.Get<ChapterRepairService>();
                ProgressText = "正在准备上下文...";

                var hints = Problems.Select(p => p.Summary).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                var repairProgress = new Progress<string>(OnProgressChanged);
                RepairedContent = await repairService.RepairChapterAsync(_chapterId, hints, _repairCts.Token, repairProgress, _repairSessionId);

                ConsistencyHint = await repairService.CheckNextChapterConsistencyAsync(_chapterId, RepairedContent);
                OnPropertyChanged(nameof(HasConsistencyHint));

                DiffViewModel.SetDiff(_chapterId, 0, ChapterContent, RepairedContent);
                State = RepairDialogState.Comparing;
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[ChapterRepairViewModel] 修复已取消: {_chapterId}");
                State = RepairDialogState.Reading;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterRepairViewModel] 修复失败: {ex.Message}");
                GlobalToast.Error("修复失败", $"修复失败：{ex.Message}");
                State = RepairDialogState.Reading;
            }
            finally
            {
                _repairCts?.Dispose();
                _repairCts = null;
            }
        }

        private void CancelRepair()
        {
            _repairCts?.Cancel();
        }

        public void Cleanup()
        {
            ServiceLocator.Get<ChapterRepairService>().ClearRepairSession(_repairSessionId);
        }

        private void OnProgressChanged(string text)
        {
            ProgressText = text;
        }

        private async Task ConfirmSaveAsync()
        {
            State = RepairDialogState.Generating;
            try
            {
                var repairService = ServiceLocator.Get<ChapterRepairService>();
                var repairProgress = new Progress<string>(OnProgressChanged);
                await repairService.SaveRepairedAsync(_chapterId, RepairedContent, repairProgress, _repairSessionId);
                GlobalToast.Success("修复完成", $"章节已成功修复并保存");
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterRepairViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
                State = RepairDialogState.Comparing;
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
