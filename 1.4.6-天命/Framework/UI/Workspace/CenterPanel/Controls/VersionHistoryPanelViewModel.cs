using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VersionHistoryPanelViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<string>? ContentReverted;
        public event Action<VersionDiff>? CompareRequested;

        private readonly ChapterVersionService _versionService;
        private string _currentChapterId = string.Empty;
        private ChapterVersionInfo? _selectedVersion;

        public VersionHistoryPanelViewModel(ChapterVersionService versionService)
        {
            _versionService = versionService;
            Versions = new ObservableCollection<ChapterVersionInfo>();

            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RedoCommand = new RelayCommand(Redo, () => CanRedo);
            RevertCommand = new RelayCommand(p => RevertToVersion(p as ChapterVersionInfo));
            CompareCommand = new RelayCommand(p => CompareWithCurrent(p as ChapterVersionInfo));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public ObservableCollection<ChapterVersionInfo> Versions { get; }

        public ChapterVersionInfo? SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (_selectedVersion != value)
                {
                    _selectedVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        public int VersionCount => Versions.Count;

        public bool CanUndo => _versionService.CanUndo(_currentChapterId);

        public bool CanRedo => _versionService.CanRedo(_currentChapterId);

        public string SummaryText => $"共 {VersionCount} 个版本，最多保留 50 个";

        public ICommand UndoCommand { get; }

        public ICommand RedoCommand { get; }

        public ICommand RevertCommand { get; }

        public ICommand CompareCommand { get; }

        #endregion

        #region 方法

        public void LoadChapterHistory(string chapterId)
        {
            _currentChapterId = chapterId;
            RefreshVersionList();
        }

        public void SaveVersion(string content, string? description = null)
        {
            if (string.IsNullOrEmpty(_currentChapterId)) return;

            _versionService.SaveVersion(_currentChapterId, content, description);
            RefreshVersionList();
        }

        private void Undo()
        {
            var content = _versionService.Undo(_currentChapterId);
            if (content != null)
            {
                ContentReverted?.Invoke(content);
                RefreshVersionList();
            }
        }

        private void Redo()
        {
            var content = _versionService.Redo(_currentChapterId);
            if (content != null)
            {
                ContentReverted?.Invoke(content);
                RefreshVersionList();
            }
        }

        private void RevertToVersion(ChapterVersionInfo? version)
        {
            if (version == null) return;

            var content = _versionService.RevertToVersion(_currentChapterId, version.Index);
            if (content != null)
            {
                ContentReverted?.Invoke(content);
                RefreshVersionList();
            }
        }

        private void CompareWithCurrent(ChapterVersionInfo? version)
        {
            if (version == null) return;

            var currentVersion = Versions.Count > 0 ? Versions[0] : null;
            if (currentVersion == null) return;

            var diff = _versionService.CompareVersions(
                _currentChapterId,
                version.Index,
                currentVersion.Index);

            if (diff != null)
            {
                CompareRequested?.Invoke(diff);
            }
        }

        private void RefreshVersionList()
        {
            Versions.Clear();

            var list = _versionService.GetVersionList(_currentChapterId);
            foreach (var v in list)
            {
                Versions.Add(v);
            }

            OnPropertyChanged(nameof(VersionCount));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(SummaryText));
        }

        #endregion
    }
}
