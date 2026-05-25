using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ThinkingPanelViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly Stopwatch _stopwatch = new();

        private bool _isThinking;
        private bool _isExpanded = true;
        private string _statusText = "等待中";

        public ThinkingPanelViewModel()
        {
            ThinkingSteps = new ObservableCollection<ThinkingStep>();
            ToggleExpandCommand = new RelayCommand(ToggleExpand);
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ElapsedTimeText => $"耗时: {_stopwatch.Elapsed.TotalSeconds:F1}s";

        public ObservableCollection<ThinkingStep> ThinkingSteps { get; }

        public ICommand ToggleExpandCommand { get; }

        #endregion

        #region 方法

        public void StartThinking()
        {
            ThinkingSteps.Clear();
            IsThinking = true;
            StatusText = "分析中...";
            _stopwatch.Restart();
        }

        public void AddStep(string icon, string title, string? detail = null)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ThinkingSteps.Add(new ThinkingStep
                {
                    Icon = icon,
                    Title = title,
                    Detail = detail ?? string.Empty,
                    Timestamp = DateTime.Now
                });

                StatusText = title;
                OnPropertyChanged(nameof(ElapsedTimeText));
            });
        }

        public void CompleteThinking(bool success = true)
        {
            _stopwatch.Stop();
            IsThinking = false;
            StatusText = success ? "完成" : "失败";
            OnPropertyChanged(nameof(ElapsedTimeText));

            AddStep(
                success ? "✅" : "❌",
                success ? "思考完成" : "思考中断",
                $"总耗时 {_stopwatch.Elapsed.TotalSeconds:F1} 秒"
            );
        }

        public void Clear()
        {
            ThinkingSteps.Clear();
            IsThinking = false;
            StatusText = "等待中";
            _stopwatch.Reset();
        }

        private void ToggleExpand()
        {
            IsExpanded = !IsExpanded;
        }

        #endregion
    }

    public class ThinkingStep
    {
        public string Icon { get; set; } = "🔍";
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public bool HasDetail => !string.IsNullOrEmpty(Detail);
    }
}
