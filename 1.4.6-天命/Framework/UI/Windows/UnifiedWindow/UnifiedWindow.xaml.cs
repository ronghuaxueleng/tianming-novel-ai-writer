using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class UnifiedWindow : Window
    {
        public event Action? CreativeWindowRequested;

        private bool _isMaximized = false;
        private bool _suppressTabChecked;

        private IAIGeneratingState? GetGeneratingState()
        {
            if (_trackedAIStateSource is IAIGeneratingState tracked)
            {
                return tracked;
            }

            if (DataContext is not UnifiedWindowViewModel vm)
            {
                return null;
            }

            return vm.CurrentView?.DataContext as IAIGeneratingState;
        }

        private bool IsAICurrentlyGenerating()
        {
            var state = GetGeneratingState();
            return state != null && (state.IsAIGenerating || state.IsBatchGenerating);
        }

        private void CancelAIIfGenerating()
        {
            var state = GetGeneratingState();
            if (state == null || (!state.IsAIGenerating && !state.IsBatchGenerating))
            {
                return;
            }

            try { ServiceLocator.Get<SKChatService>().CancelCurrentRequest(); }
            catch { }

            var cmd = state.CancelBatchGenerationCommand;
            if (cmd?.CanExecute(null) == true) cmd.Execute(null);
        }

        private bool _isStandaloneMode = false;
        public bool IsStandaloneMode
        {
            get => _isStandaloneMode;
            set
            {
                if (_isStandaloneMode == value) return;
                _isStandaloneMode = value;
                ShowInTaskbar = value;

                if (value)
                {
                    Owner = null;
                }
                else
                {
                    EnsureOwner();
                }
            }
        }

        private bool _isPinned = false;
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private UnifiedWindowSettings _settings;
        private INotifyPropertyChanged? _trackedAIStateSource;

        private UserControl? _activeView;
        private readonly HashSet<UserControl> _hostedViews = new();

        private void SwitchToView(UserControl? newView)
        {
            if (newView == null || newView == _activeView) return;
            if (_activeView != null)
                _activeView.Visibility = Visibility.Collapsed;

            if (newView.Parent is DependencyObject parent && !ReferenceEquals(parent, ViewHostPanel))
            {
                switch (parent)
                {
                    case Panel panel:
                        panel.Children.Remove(newView);
                        break;
                    case Decorator decorator:
                        if (ReferenceEquals(decorator.Child, newView)) decorator.Child = null;
                        break;
                    case ContentPresenter presenter:
                        if (ReferenceEquals(presenter.Content, newView)) presenter.Content = null;
                        break;
                    case ContentControl contentControl:
                        if (ReferenceEquals(contentControl.Content, newView)) contentControl.Content = null;
                        break;
                    case ItemsControl itemsControl:
                        if (itemsControl.Items.Contains(newView)) itemsControl.Items.Remove(newView);
                        break;
                }
            }

            if (_hostedViews.Add(newView))
            {
                newView.HorizontalAlignment = HorizontalAlignment.Stretch;
                newView.VerticalAlignment = VerticalAlignment.Stretch;
            }

            if (!ReferenceEquals(newView.Parent, ViewHostPanel))
            {
                ViewHostPanel.Children.Add(newView);
            }
            newView.Visibility = Visibility.Visible;
            _activeView = newView;
        }

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[UnifiedWindow] {key}: {ex.Message}");
        }

        public UnifiedWindow()
        {
            InitializeComponent();

            DataContext = ServiceLocator.Get<UnifiedWindowViewModel>();

            _settings = UnifiedWindowSettings.Load();
            LoadWindowState();

            Loaded += OnWindowLoaded;
            Activated += (_, __) =>
            {
                if (!_isStandaloneMode)
                {
                    EnsureOwner();
                }
            };
            Closing += OnUnifiedWindowClosing;

            if (DataContext is UnifiedWindowViewModel vm)
                SubscribeViewModelForOverlay(vm);

            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is UnifiedWindowViewModel oldVm)
                    UnsubscribeViewModelForOverlay(oldVm);
                if (e.NewValue is UnifiedWindowViewModel newVm)
                    SubscribeViewModelForOverlay(newVm);
            };

            Closed += (_, __) =>
            {
                if (DataContext is UnifiedWindowViewModel vmToCancel)
                {
                    vmToCancel.CancelPreWarm();
                }
                CleanupOverlaySubscriptions();
            };
        }

        private void OnUnifiedWindowClosing(object? sender, CancelEventArgs e)
        {
            if (!_isStandaloneMode)
            {
                return;
            }

            e.Cancel = true;
            CancelAIIfGenerating();
            SaveWindowState();

            try
            {
                CreativeWindowRequested?.Invoke();
            }
            catch
            {
            }

            Hide();
        }

        private void SubscribeViewModelForOverlay(UnifiedWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChangedForOverlay;
            SwitchToView(vm.CurrentView);
            UpdateTrackedAIStateSource(vm.CurrentView);
        }

        private void UnsubscribeViewModelForOverlay(UnifiedWindowViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChangedForOverlay;
        }

        private void CleanupOverlaySubscriptions()
        {
            if (DataContext is UnifiedWindowViewModel vm)
                UnsubscribeViewModelForOverlay(vm);
            DetachTrackedAIStateSource();
        }

        private void OnViewModelPropertyChangedForOverlay(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(UnifiedWindowViewModel.CurrentView)) return;
            if (sender is UnifiedWindowViewModel vmSender)
            {
                SwitchToView(vmSender.CurrentView);
                CancelAIIfGenerating();
                UpdateTrackedAIStateSource(vmSender.CurrentView);
            }
        }

        private void UpdateTrackedAIStateSource(UserControl? currentView)
        {
            DetachTrackedAIStateSource();
            if (currentView?.DataContext is INotifyPropertyChanged npc and IAIGeneratingState)
            {
                _trackedAIStateSource = npc;
                _trackedAIStateSource.PropertyChanged += OnAIStatePropertyChangedForOverlay;
            }
            Dispatcher.InvokeAsync(SyncAIGenerateOverlay, DispatcherPriority.Background);
        }

        private void DetachTrackedAIStateSource()
        {
            if (_trackedAIStateSource == null) return;
            _trackedAIStateSource.PropertyChanged -= OnAIStatePropertyChangedForOverlay;
            _trackedAIStateSource = null;
        }

        private void OnAIStatePropertyChangedForOverlay(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IAIGeneratingState.IsAIGenerating)
                || e.PropertyName == nameof(IAIGeneratingState.IsBatchGenerating)
                || e.PropertyName == nameof(IAIGeneratingState.BatchProgressText))
            {
                Dispatcher.InvokeAsync(SyncAIGenerateOverlay, DispatcherPriority.Normal);
            }
        }

        private void EnsureOwner()
        {
            try
            {
                if (_isStandaloneMode)
                {
                    return;
                }

                if (!IsLoaded)
                    return;

                if (Owner != null && Owner.IsVisible && Owner.WindowState != WindowState.Minimized)
                {
                    return;
                }

                Window? resolvedOwner = null;

                try
                {
                    if (Application.Current != null)
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            if (w == this) continue;
                            if (!w.IsVisible || w.WindowState == WindowState.Minimized) continue;
                            if (w.IsActive)
                            {
                                resolvedOwner = w;
                                break;
                            }
                            resolvedOwner ??= w;
                        }
                    }
                }
                catch
                {
                }

                resolvedOwner ??= Application.Current?.MainWindow;

                if (resolvedOwner != null)
                {
                    Owner = resolvedOwner;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(EnsureOwner), ex);
            }
        }

        private void SyncAIGenerateOverlay()
        {
            if (AIGenerateOverlay == null)
            {
                return;
            }

            TextBlock? overlayTextBlock = null;
            if (AIGenerateOverlay.OverlayContent is StackPanel sp && sp.Children.Count > 1)
            {
                overlayTextBlock = sp.Children[1] as TextBlock;
            }

            bool isGenerating = false;
            bool isBatch = false;
            string? batchText = null;

            if (DataContext is UnifiedWindowViewModel windowVm
                && windowVm.CurrentView?.DataContext is IAIGeneratingState state)
            {
                isGenerating = state.IsAIGenerating;
                isBatch = state.IsBatchGenerating;
                batchText = state.BatchProgressText;
            }

            var shouldBusy = isGenerating || isBatch;
            AIGenerateOverlay.IsBusy = shouldBusy;

            if (overlayTextBlock == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(batchText))
            {
                overlayTextBlock.Text = batchText;
                overlayTextBlock.Foreground = isBatch ? Brushes.Black : Brushes.White;
            }
            else
            {
                overlayTextBlock.Text = "正在生成...";
                overlayTextBlock.Foreground = Brushes.White;
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            EnsureOwner();
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                _ = viewModel.PreWarmAllViewsAsync();
            }
        }

        private void InitializeWindowPosition()
        {
            var workArea = SystemParameters.WorkArea;

            Left = (workArea.Width - Width) / 2 + workArea.Left;
            Top = (workArea.Height - Height) / 2 + workArea.Top;
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                }
                else
                {
                    if (_isMaximized)
                    {
                        RestoreWindow();

                        var point = e.GetPosition(this);
                        Left = e.GetPosition(null).X - point.X;
                        Top = e.GetPosition(null).Y - point.Y;
                    }

                    DragMove();
                }
            }
        }

        private void OnPinToggleClick(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            Topmost = _isPinned;
            UpdatePinButtonState();
            _settings.IsPinned = _isPinned;
            _settings.Save();
        }

        private void UpdatePinButtonState()
        {
            if (PinButtonContent == null || PinButtonLabel == null)
            {
                return;
            }

            if (_isPinned)
            {
                PinButtonContent.Opacity = 1.0;
                PinButtonLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryColor");
            }
            else
            {
                PinButtonContent.Opacity = 0.4;
                PinButtonLabel.ClearValue(TextBlock.ForegroundProperty);
            }
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void OnCreativeWindowClick(object sender, RoutedEventArgs e)
        {
            if (IsAICurrentlyGenerating())
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "AI正在生成中，切换到创作台将中断本次生成。\n\n是否仍要切换？",
                    "切换确认");
                if (confirmed != true)
                {
                    return;
                }

                CancelAIIfGenerating();
            }
            CreativeWindowRequested?.Invoke();
            Hide();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            if (_isStandaloneMode)
            {
                WindowState = WindowState.Minimized;
            }
            else
            {
                Hide();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CancelAIIfGenerating();

            SaveWindowState();

            Owner?.Activate();

            Close();
        }

        private void ToggleMaximize()
        {
            if (_isMaximized)
            {
                RestoreWindow();
            }
            else
            {
                MaximizeWindow();
            }
        }

        private void MaximizeWindow()
        {
            _normalLeft = Left;
            _normalTop = Top;
            _normalWidth = Width;
            _normalHeight = Height;

            var workArea = SystemParameters.WorkArea;

            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;

            _isMaximized = true;

            UpdateMaximizeButton();
        }

        private void RestoreWindow()
        {
            Left = _normalLeft;
            Top = _normalTop;
            Width = _normalWidth;
            Height = _normalHeight;

            _isMaximized = false;

            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            if (MaximizeButton != null)
            {
                var stackPanel = MaximizeButton.Content as StackPanel;
                if (stackPanel != null && stackPanel.Children.Count > 0)
                {
                    var iconText = stackPanel.Children[0] as TextBlock;
                    var labelText = stackPanel.Children[1] as TextBlock;

                    if (iconText != null && labelText != null)
                    {
                        if (_isMaximized)
                        {
                            iconText.Text = "❐";
                            labelText.Text = "还原";
                        }
                        else
                        {
                            iconText.Text = "□";
                            labelText.Text = "最大化";
                        }
                    }
                }
            }
        }

        private void OnTabChecked(object sender, RoutedEventArgs e)
        {
            if (_suppressTabChecked)
            {
                return;
            }

            var radioButton = sender as RadioButton;
            if (radioButton?.DataContext == null) return;

            var viewModel = DataContext as UnifiedWindowViewModel;
            if (viewModel == null) return;

            var targetTab = radioButton.DataContext as UnifiedWindowViewModel.SettingsTab;

            if (IsAICurrentlyGenerating())
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "AI正在生成中，切换功能将中断本次生成。\n\n是否仍要切换？",
                    "切换确认");
                if (confirmed != true)
                {
                    _suppressTabChecked = true;
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (targetTab != null)
                            {
                                targetTab.IsSelected = false;
                            }

                            if (viewModel.SelectedTab != null)
                            {
                                viewModel.SelectedTab.IsSelected = true;
                            }
                        }
                        finally
                        {
                            _suppressTabChecked = false;
                        }
                    }, DispatcherPriority.Render);
                    return;
                }

                CancelAIIfGenerating();
            }

            viewModel.SelectedTab = radioButton.DataContext as UnifiedWindowViewModel.SettingsTab;
        }

        private void OnPersonalModeClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                if (IsAICurrentlyGenerating())
                {
                    var confirmed = StandardDialog.ShowConfirm(
                        "AI正在生成中，切换模式将中断本次生成。\n\n是否仍要切换？",
                        "切换确认");
                    if (confirmed != true)
                    {
                        return;
                    }

                    CancelAIIfGenerating();
                }
                viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Settings;
                UpdateModeButtonStyles(true);
                TM.App.Log("[UnifiedWindow] 切换到个人模式");
            }
        }

        private void OnWritingModeClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                if (IsAICurrentlyGenerating())
                {
                    var confirmed = StandardDialog.ShowConfirm(
                        "AI正在生成中，切换模式将中断本次生成。\n\n是否仍要切换？",
                        "切换确认");
                    if (confirmed != true)
                    {
                        return;
                    }

                    CancelAIIfGenerating();
                }
                viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Writing;
                UpdateModeButtonStyles(false);
                TM.App.Log("[UnifiedWindow] 切换到写作模式");
            }
        }

        private void OnCancelAIGenerationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ServiceLocator.Get<SKChatService>().CancelCurrentRequest();
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(OnCancelAIGenerationClick), ex);
                return;
            }

            if (DataContext is not UnifiedWindowViewModel windowVm)
            {
                return;
            }

            var view = windowVm.CurrentView as FrameworkElement;
            var vm = view?.DataContext;
            if (vm == null)
            {
                return;
            }

            if (vm is not IAIGeneratingState state)
            {
                return;
            }

            var cmd = state.CancelBatchGenerationCommand;
            if (cmd == null || !cmd.CanExecute(null))
            {
                return;
            }

            cmd.Execute(null);
        }

        private void UpdateModeButtonStyles(bool isPersonalMode)
        {
            if (PersonalModeButton != null && WritingModeButton != null && 
                PersonalModeText != null && WritingModeText != null)
            {
                if (isPersonalMode)
                {
                    PersonalModeButton.SetResourceReference(Border.BackgroundProperty, "PrimaryColor");
                    PersonalModeText.Foreground = System.Windows.Media.Brushes.White;
                    PersonalModeText.FontWeight = System.Windows.FontWeights.Medium;

                    WritingModeButton.Background = System.Windows.Media.Brushes.Transparent;
                    WritingModeText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    WritingModeText.FontWeight = System.Windows.FontWeights.Normal;
                }
                else
                {
                    WritingModeButton.SetResourceReference(Border.BackgroundProperty, "PrimaryColor");
                    WritingModeText.Foreground = System.Windows.Media.Brushes.White;
                    WritingModeText.FontWeight = System.Windows.FontWeights.Medium;

                    PersonalModeButton.Background = System.Windows.Media.Brushes.Transparent;
                    PersonalModeText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    PersonalModeText.FontWeight = System.Windows.FontWeights.Normal;
                }
            }
        }

        private void OnGridSplitterDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (LeftColumn != null)
            {
                var width = LeftColumn.Width.Value;
                _settings.LeftColumnWidth = width;
                _settings.Save();
                TM.App.Log($"[UnifiedWindow] 左侧栏宽度已保存: {width}");
            }
        }

        private void SaveWindowState()
        {
            try
            {
                if (!_isMaximized)
                {
                    _settings.Width = Width;
                    _settings.Height = Height;
                }
                else
                {
                    _settings.Width = _normalWidth;
                    _settings.Height = _normalHeight;
                }

                _settings.IsMaximized = _isMaximized;

                if (LeftColumn != null)
                {
                    _settings.LeftColumnWidth = LeftColumn.Width.Value;
                }

                if (DataContext is UnifiedWindowViewModel viewModel)
                {
                    _settings.CurrentMode = viewModel.CurrentMode == UnifiedWindowViewModel.WindowMode.Writing ? "Writing" : "Settings";
                    _settings.SelectedTabName = viewModel.SelectedTab?.ModuleName ?? "";
                }

                _settings.IsPinned = _isPinned;

                _settings.Save();

                TM.App.Log($"[UnifiedWindow] 窗口状态已保存 - 位置: ({_settings.Left}, {_settings.Top}), 大小: {_settings.Width}x{_settings.Height}, 模式: {_settings.CurrentMode}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 保存窗口状态异常: {ex.Message}");
            }
        }

        private void LoadWindowState()
        {
            try
            {
                if (_settings.Width > 0 && _settings.Height > 0)
                {
                    Width = _settings.Width;
                    Height = _settings.Height;
                }

                InitializeWindowPosition();

                if (LeftColumn != null && _settings.LeftColumnWidth > 0)
                {
                    LeftColumn.Width = new GridLength(_settings.LeftColumnWidth);
                }

                if (_settings.IsMaximized)
                {
                    Dispatcher.BeginInvoke(new Action(() => MaximizeWindow()), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                _isPinned = _settings.IsPinned;
                Topmost = _isPinned;
                Dispatcher.BeginInvoke(new Action(UpdatePinButtonState), System.Windows.Threading.DispatcherPriority.Loaded);

                if (DataContext is UnifiedWindowViewModel viewModel)
                {
                    var targetMode = _settings.CurrentMode == "Writing"
                        ? UnifiedWindowViewModel.WindowMode.Writing
                        : UnifiedWindowViewModel.WindowMode.Settings;
                    viewModel.CurrentMode = targetMode;
                    UpdateModeButtonStyles(targetMode == UnifiedWindowViewModel.WindowMode.Settings);

                    if (!string.IsNullOrEmpty(_settings.SelectedTabName))
                    {
                        var tab = viewModel.Tabs.FirstOrDefault(
                            t => t.ModuleName == _settings.SelectedTabName);
                        if (tab != null)
                            viewModel.SelectedTab = tab;
                    }
                }

                TM.App.Log($"[UnifiedWindow] 窗口状态已加载 - 位置: ({Left}, {Top}), 大小: {Width}x{Height}, 模式: {_settings.CurrentMode}, Tab: {_settings.SelectedTabName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载窗口状态异常: {ex.Message}");
                InitializeWindowPosition();
            }
        }
    }
}
