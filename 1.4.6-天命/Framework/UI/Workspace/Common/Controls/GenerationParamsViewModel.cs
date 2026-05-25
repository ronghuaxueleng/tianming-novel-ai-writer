using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class GenerationParamsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? SaveCompleted;

        private string _statusMessage = "";
        private bool _hasStatusMessage;

        public GenerationParamsViewModel()
        {
            SaveCommand = new AsyncRelayCommand(async () => await SaveAsync());
            ResetCommand = new RelayCommand(Reset);
            Preset128KCommand = new RelayCommand(ApplyPreset128K);
            Preset256KCommand = new RelayCommand(ApplyPreset256K);
            Preset1MCommand = new RelayCommand(ApplyPreset1M);
            LoadFromConfig();
        }

        #region 活跃窗口

        private int _activeEntityWindowChapters;
        public int ActiveEntityWindowChapters
        {
            get => _activeEntityWindowChapters;
            set { _activeEntityWindowChapters = value; OnPropertyChanged(); }
        }

        private int _activeEntityWindowMaxCount;
        public int ActiveEntityWindowMaxCount
        {
            get => _activeEntityWindowMaxCount;
            set { _activeEntityWindowMaxCount = value; OnPropertyChanged(); }
        }

        private int _summaryRecentWindowCount;
        public int SummaryRecentWindowCount
        {
            get => _summaryRecentWindowCount;
            set { _summaryRecentWindowCount = value; OnPropertyChanged(); }
        }

        private int _previousSummaryCount;
        public int PreviousSummaryCount
        {
            get => _previousSummaryCount;
            set { _previousSummaryCount = value; OnPropertyChanged(); }
        }

        #endregion

        #region 里程碑

        private int _milestoneAnchorInterval;
        public int MilestoneAnchorInterval
        {
            get => _milestoneAnchorInterval;
            set { _milestoneAnchorInterval = value; OnPropertyChanged(); }
        }

        private int _volumeMilestoneMaxChars;
        public int VolumeMilestoneMaxChars
        {
            get => _volumeMilestoneMaxChars;
            set { _volumeMilestoneMaxChars = value; OnPropertyChanged(); }
        }

        private int _volumeMilestoneTailRecentCount;
        public int VolumeMilestoneTailRecentCount
        {
            get => _volumeMilestoneTailRecentCount;
            set { _volumeMilestoneTailRecentCount = value; OnPropertyChanged(); }
        }

        #endregion

        #region 跨卷注入

        private int _milestoneMaxPreviousVolumes;
        public int MilestoneMaxPreviousVolumes
        {
            get => _milestoneMaxPreviousVolumes;
            set { _milestoneMaxPreviousVolumes = value; OnPropertyChanged(); }
        }

        private int _archiveMaxPreviousVolumes;
        public int ArchiveMaxPreviousVolumes
        {
            get => _archiveMaxPreviousVolumes;
            set { _archiveMaxPreviousVolumes = value; OnPropertyChanged(); }
        }

        #endregion

        #region 快照注入上限

        private int _snapshotMaxFactionInject;
        public int SnapshotMaxFactionInject
        {
            get => _snapshotMaxFactionInject;
            set { _snapshotMaxFactionInject = value; OnPropertyChanged(); }
        }

        private int _snapshotMaxItemInject;
        public int SnapshotMaxItemInject
        {
            get => _snapshotMaxItemInject;
            set { _snapshotMaxItemInject = value; OnPropertyChanged(); }
        }

        private int _snapshotMaxTimelineInject;
        public int SnapshotMaxTimelineInject
        {
            get => _snapshotMaxTimelineInject;
            set { _snapshotMaxTimelineInject = value; OnPropertyChanged(); }
        }

        #endregion

        #region 存档注入上限

        private int _archiveInjectMaxCharacterStates;
        public int ArchiveInjectMaxCharacterStates
        {
            get => _archiveInjectMaxCharacterStates;
            set { _archiveInjectMaxCharacterStates = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxConflictProgress;
        public int ArchiveInjectMaxConflictProgress
        {
            get => _archiveInjectMaxConflictProgress;
            set { _archiveInjectMaxConflictProgress = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxFieldChars;
        public int ArchiveInjectMaxFieldChars
        {
            get => _archiveInjectMaxFieldChars;
            set { _archiveInjectMaxFieldChars = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxItemStates;
        public int ArchiveInjectMaxItemStates
        {
            get => _archiveInjectMaxItemStates;
            set { _archiveInjectMaxItemStates = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxForeshadowingStatus;
        public int ArchiveInjectMaxForeshadowingStatus
        {
            get => _archiveInjectMaxForeshadowingStatus;
            set { _archiveInjectMaxForeshadowingStatus = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxTimelineEntries;
        public int ArchiveInjectMaxTimelineEntries
        {
            get => _archiveInjectMaxTimelineEntries;
            set { _archiveInjectMaxTimelineEntries = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxCharacterLocations;
        public int ArchiveInjectMaxCharacterLocations
        {
            get => _archiveInjectMaxCharacterLocations;
            set { _archiveInjectMaxCharacterLocations = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxFactionStates;
        public int ArchiveInjectMaxFactionStates
        {
            get => _archiveInjectMaxFactionStates;
            set { _archiveInjectMaxFactionStates = value; OnPropertyChanged(); }
        }

        private int _archiveInjectMaxLocationStates;
        public int ArchiveInjectMaxLocationStates
        {
            get => _archiveInjectMaxLocationStates;
            set { _archiveInjectMaxLocationStates = value; OnPropertyChanged(); }
        }

        #endregion

        #region 状态

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); HasStatusMessage = !string.IsNullOrEmpty(value); }
        }

        public bool HasStatusMessage
        {
            get => _hasStatusMessage;
            set { _hasStatusMessage = value; OnPropertyChanged(); }
        }

        #endregion

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand Preset128KCommand { get; }
        public ICommand Preset256KCommand { get; }
        public ICommand Preset1MCommand { get; }

        private void LoadFromConfig()
        {
            ActiveEntityWindowChapters = LayeredContextConfig.ActiveEntityWindowChapters;
            ActiveEntityWindowMaxCount = LayeredContextConfig.ActiveEntityWindowMaxCount;
            SummaryRecentWindowCount = LayeredContextConfig.SummaryRecentWindowCount;
            MilestoneAnchorInterval = LayeredContextConfig.MilestoneAnchorInterval;
            VolumeMilestoneMaxChars = LayeredContextConfig.VolumeMilestoneMaxChars;
            VolumeMilestoneTailRecentCount = LayeredContextConfig.VolumeMilestoneTailRecentCount;
            MilestoneMaxPreviousVolumes = LayeredContextConfig.MilestoneMaxPreviousVolumes;
            ArchiveMaxPreviousVolumes = LayeredContextConfig.ArchiveMaxPreviousVolumes;
            PreviousSummaryCount = LayeredContextConfig.PreviousSummaryCount;
            SnapshotMaxFactionInject = LayeredContextConfig.SnapshotMaxFactionInject;
            SnapshotMaxItemInject = LayeredContextConfig.SnapshotMaxItemInject;
            SnapshotMaxTimelineInject = LayeredContextConfig.SnapshotMaxTimelineInject;
            ArchiveInjectMaxCharacterStates = LayeredContextConfig.ArchiveInjectMaxCharacterStates;
            ArchiveInjectMaxConflictProgress = LayeredContextConfig.ArchiveInjectMaxConflictProgress;
            ArchiveInjectMaxFieldChars = LayeredContextConfig.ArchiveInjectMaxFieldChars;
            ArchiveInjectMaxItemStates = LayeredContextConfig.ArchiveInjectMaxItemStates;
            ArchiveInjectMaxForeshadowingStatus = LayeredContextConfig.ArchiveInjectMaxForeshadowingStatus;
            ArchiveInjectMaxTimelineEntries = LayeredContextConfig.ArchiveInjectMaxTimelineEntries;
            ArchiveInjectMaxCharacterLocations = LayeredContextConfig.ArchiveInjectMaxCharacterLocations;
            ArchiveInjectMaxFactionStates = LayeredContextConfig.ArchiveInjectMaxFactionStates;
            ArchiveInjectMaxLocationStates = LayeredContextConfig.ArchiveInjectMaxLocationStates;
        }

        private void ApplyToConfig()
        {
            LayeredContextConfig.ActiveEntityWindowChapters = ActiveEntityWindowChapters;
            LayeredContextConfig.ActiveEntityWindowMaxCount = ActiveEntityWindowMaxCount;
            LayeredContextConfig.SummaryRecentWindowCount = SummaryRecentWindowCount;
            LayeredContextConfig.MilestoneAnchorInterval = MilestoneAnchorInterval;
            LayeredContextConfig.VolumeMilestoneMaxChars = VolumeMilestoneMaxChars;
            LayeredContextConfig.VolumeMilestoneTailRecentCount = VolumeMilestoneTailRecentCount;
            LayeredContextConfig.MilestoneMaxPreviousVolumes = MilestoneMaxPreviousVolumes;
            LayeredContextConfig.ArchiveMaxPreviousVolumes = ArchiveMaxPreviousVolumes;
            LayeredContextConfig.PreviousSummaryCount = PreviousSummaryCount;
            LayeredContextConfig.SnapshotMaxFactionInject = SnapshotMaxFactionInject;
            LayeredContextConfig.SnapshotMaxItemInject = SnapshotMaxItemInject;
            LayeredContextConfig.SnapshotMaxTimelineInject = SnapshotMaxTimelineInject;
            LayeredContextConfig.ArchiveInjectMaxCharacterStates = ArchiveInjectMaxCharacterStates;
            LayeredContextConfig.ArchiveInjectMaxConflictProgress = ArchiveInjectMaxConflictProgress;
            LayeredContextConfig.ArchiveInjectMaxFieldChars = ArchiveInjectMaxFieldChars;
            LayeredContextConfig.ArchiveInjectMaxItemStates = ArchiveInjectMaxItemStates;
            LayeredContextConfig.ArchiveInjectMaxForeshadowingStatus = ArchiveInjectMaxForeshadowingStatus;
            LayeredContextConfig.ArchiveInjectMaxTimelineEntries = ArchiveInjectMaxTimelineEntries;
            LayeredContextConfig.ArchiveInjectMaxCharacterLocations = ArchiveInjectMaxCharacterLocations;
            LayeredContextConfig.ArchiveInjectMaxFactionStates = ArchiveInjectMaxFactionStates;
            LayeredContextConfig.ArchiveInjectMaxLocationStates = ArchiveInjectMaxLocationStates;
        }

        private async Task SaveAsync()
        {
            try
            {
                ApplyToConfig();
                await LayeredContextConfig.SaveToStorageAsync();
                StatusMessage = "已保存";
                TM.App.Log("[GenerationParams] 参数已保存");
                SaveCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
                TM.App.Log($"[GenerationParams] 保存失败: {ex.Message}");
            }
        }

        private void Reset()
        {
            ActiveEntityWindowChapters = 8;
            ActiveEntityWindowMaxCount = 25;
            SummaryRecentWindowCount = 30;
            MilestoneAnchorInterval = 8;
            VolumeMilestoneMaxChars = 20000;
            VolumeMilestoneTailRecentCount = 15;
            MilestoneMaxPreviousVolumes = 12;
            ArchiveMaxPreviousVolumes = 8;
            PreviousSummaryCount = 30;
            SnapshotMaxFactionInject = 30;
            SnapshotMaxItemInject = 50;
            SnapshotMaxTimelineInject = 5;
            ArchiveInjectMaxCharacterStates = 60;
            ArchiveInjectMaxConflictProgress = 25;
            ArchiveInjectMaxFieldChars = 300;
            ArchiveInjectMaxItemStates = 50;
            ArchiveInjectMaxForeshadowingStatus = 40;
            ArchiveInjectMaxTimelineEntries = 10;
            ArchiveInjectMaxCharacterLocations = 50;
            ArchiveInjectMaxFactionStates = 20;
            ArchiveInjectMaxLocationStates = 20;
            StatusMessage = "已恢复默认值（点击保存生效）";
        }

        private void ApplyPreset128K()
        {
            ActiveEntityWindowChapters = 6;
            ActiveEntityWindowMaxCount = 20;
            SummaryRecentWindowCount = 20;
            MilestoneAnchorInterval = 10;
            VolumeMilestoneMaxChars = 15000;
            VolumeMilestoneTailRecentCount = 15;
            MilestoneMaxPreviousVolumes = 10;
            ArchiveMaxPreviousVolumes = 5;
            PreviousSummaryCount = 20;
            SnapshotMaxFactionInject = 30;
            SnapshotMaxItemInject = 50;
            SnapshotMaxTimelineInject = 3;
            ArchiveInjectMaxCharacterStates = 50;
            ArchiveInjectMaxConflictProgress = 20;
            ArchiveInjectMaxFieldChars = 200;
            ArchiveInjectMaxItemStates = 30;
            ArchiveInjectMaxForeshadowingStatus = 20;
            ArchiveInjectMaxTimelineEntries = 5;
            ArchiveInjectMaxCharacterLocations = 30;
            ArchiveInjectMaxFactionStates = 15;
            ArchiveInjectMaxLocationStates = 15;
            StatusMessage = "已应用 128K 预设（GPT-4 / Claude 3.5 等）";
        }

        private void ApplyPreset256K()
        {
            ActiveEntityWindowChapters = 12;
            ActiveEntityWindowMaxCount = 32;
            SummaryRecentWindowCount = 45;
            MilestoneAnchorInterval = 7;
            VolumeMilestoneMaxChars = 28000;
            VolumeMilestoneTailRecentCount = 20;
            MilestoneMaxPreviousVolumes = 15;
            ArchiveMaxPreviousVolumes = 10;
            PreviousSummaryCount = 40;
            SnapshotMaxFactionInject = 45;
            SnapshotMaxItemInject = 80;
            SnapshotMaxTimelineInject = 5;
            ArchiveInjectMaxCharacterStates = 90;
            ArchiveInjectMaxConflictProgress = 35;
            ArchiveInjectMaxFieldChars = 400;
            ArchiveInjectMaxItemStates = 60;
            ArchiveInjectMaxForeshadowingStatus = 35;
            ArchiveInjectMaxTimelineEntries = 10;
            ArchiveInjectMaxCharacterLocations = 50;
            ArchiveInjectMaxFactionStates = 20;
            ArchiveInjectMaxLocationStates = 20;
            StatusMessage = "已应用 256K 预设（Claude Sonnet / Gemini Pro 等）";
        }

        private void ApplyPreset1M()
        {
            ActiveEntityWindowChapters = 18;
            ActiveEntityWindowMaxCount = 45;
            SummaryRecentWindowCount = 70;
            MilestoneAnchorInterval = 5;
            VolumeMilestoneMaxChars = 40000;
            VolumeMilestoneTailRecentCount = 30;
            MilestoneMaxPreviousVolumes = 22;
            ArchiveMaxPreviousVolumes = 15;
            PreviousSummaryCount = 60;
            SnapshotMaxFactionInject = 65;
            SnapshotMaxItemInject = 120;
            SnapshotMaxTimelineInject = 8;
            ArchiveInjectMaxCharacterStates = 130;
            ArchiveInjectMaxConflictProgress = 55;
            ArchiveInjectMaxFieldChars = 600;
            ArchiveInjectMaxItemStates = 100;
            ArchiveInjectMaxForeshadowingStatus = 60;
            ArchiveInjectMaxTimelineEntries = 15;
            ArchiveInjectMaxCharacterLocations = 80;
            ArchiveInjectMaxFactionStates = 30;
            ArchiveInjectMaxLocationStates = 30;
            StatusMessage = "已应用 1M+ 预设（Gemini 1.5/2.0 / GPT-5 等）";
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
