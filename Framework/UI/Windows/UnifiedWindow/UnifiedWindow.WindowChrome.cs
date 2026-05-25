using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.Common.ViewModels;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Windows
{
    public partial class UnifiedWindow
    {
        private void SyncAIGenerateOverlay()
        {
            if (AIGenerateOverlay == null)
            {
                return;
            }

            TextBlock? overlayTextBlock = null;
            Button? overlayCancelButton = null;
            if (AIGenerateOverlay.OverlayContent is StackPanel sp && sp.Children.Count > 1)
            {
                overlayTextBlock = sp.Children[1] as TextBlock;
                if (sp.Children.Count > 2)
                    overlayCancelButton = sp.Children[2] as Button;
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

            var textBrush = _cachedOverlayTextBrush
                ??= (Application.Current?.TryFindResource("TextPrimary") as Brush) ?? Brushes.Black;

            if (!string.IsNullOrWhiteSpace(batchText))
            {
                overlayTextBlock.Text = batchText;
                overlayTextBlock.Foreground = textBrush;
            }
            else
            {
                overlayTextBlock.Text = "正在生成...";
                overlayTextBlock.Foreground = textBrush;
            }

            if (overlayCancelButton != null)
            {
                bool isTestMode = batchText != null && (batchText.Contains("测试") || batchText.Contains("获取模型"));
                overlayCancelButton.Content = isTestMode ? "取消测试" : "取消生成";
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            EnsureOwner();
            StartPreWarm();
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
            try { UnifiedWindowSettings.Update(settings => settings.IsPinned = _isPinned); }
            catch (Exception ex) { TM.App.Log($"[UnifiedWindow] 置顶设置保存失败（忽略）: {ex.Message}"); }
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

        private void OnPopupCreativeClick(object sender, RoutedEventArgs e)
        {
            if (!ConfirmAndCancelAIIfGenerating("切换到创作台"))
                return;
            CreativeWindowRequested?.Invoke();
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

            if (_isStandaloneMode || Owner?.IsVisible != true)
            {
                try
                {
                    CreativeWindowRequested?.Invoke();
                }
                catch
                {
                }

                Hide();
                return;
            }

            Owner?.Activate();
            Hide();
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
            if (MaximizeButton == null)
            {
                return;
            }

            if (MaximizeButton.Content is not StackPanel stackPanel || stackPanel.Children.Count < 2)
            {
                return;
            }

            var icon = stackPanel.Children[0] as Image;
            var labelText = stackPanel.Children[1] as TextBlock;

            if (labelText == null)
            {
                return;
            }

            if (_isMaximized)
            {
                labelText.Text = "还原";
                icon?.SetResourceReference(Image.SourceProperty, "Icon.WindowRestore");
            }
            else
            {
                labelText.Text = "最大化";
                icon?.SetResourceReference(Image.SourceProperty, "Icon.WindowMaximize");
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

            if (!ConfirmAndCancelAIIfGenerating("切换功能"))
            {
                _suppressTabChecked = true;
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (targetTab != null)
                            targetTab.IsSelected = false;
                        if (viewModel.SelectedTab != null)
                            viewModel.SelectedTab.IsSelected = true;
                    }
                    finally
                    {
                        _suppressTabChecked = false;
                    }
                }, DispatcherPriority.Render);
                return;
            }

            viewModel.SelectedTab = radioButton.DataContext as UnifiedWindowViewModel.SettingsTab;

            if (!ReferenceEquals(viewModel.SelectedTab, targetTab))
            {
                _suppressTabChecked = true;
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (targetTab != null) targetTab.IsSelected = false;
                        if (viewModel.SelectedTab != null) viewModel.SelectedTab.IsSelected = true;
                    }
                    finally
                    {
                        _suppressTabChecked = false;
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void OnPersonalModeClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                if (!ConfirmAndCancelAIIfGenerating("切换模式"))
                    return;
                viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Settings;
                UpdateModeButtonStyles(true);
                TM.App.Log("[UnifiedWindow] 切换到个人模式");
            }
        }

        private void OnWritingModeClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                if (!ConfirmAndCancelAIIfGenerating("切换模式"))
                    return;
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
                try { UnifiedWindowSettings.Update(settings => settings.LeftColumnWidth = width); }
                catch (Exception ex) { TM.App.Log($"[UnifiedWindow] 分隔栏宽度保存失败（忽略）: {ex.Message}"); }
                TM.App.Log($"[UnifiedWindow] 左侧栏宽度已保存: {width}");
            }
        }

        private void SaveWindowState()
        {
            try
            {
                _settings = UnifiedWindowSettings.Update(settings =>
                {
                    if (!_isMaximized)
                    {
                        settings.Left = Left;
                        settings.Top = Top;
                        settings.Width = Width;
                        settings.Height = Height;
                    }
                    else
                    {
                        settings.Left = _normalLeft;
                        settings.Top = _normalTop;
                        settings.Width = _normalWidth;
                        settings.Height = _normalHeight;
                    }

                    settings.IsMaximized = _isMaximized;

                    if (LeftColumn != null)
                    {
                        settings.LeftColumnWidth = LeftColumn.Width.Value;
                    }

                    if (DataContext is UnifiedWindowViewModel viewModel)
                    {
                        settings.CurrentMode = viewModel.CurrentMode == UnifiedWindowViewModel.WindowMode.Writing ? "Writing" : "Settings";
                        settings.SelectedTabName = viewModel.SelectedTab?.ModuleName ?? "";
                    }

                    settings.IsPinned = _isPinned;
                });

                TM.App.Log($"[UnifiedWindow] 窗口状态已保存 - 位置: ({_settings.Left}, {_settings.Top}), 大小: {_settings.Width}x{_settings.Height}, 模式: {_settings.CurrentMode}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 保存窗口状态异常: {ex.Message}");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _settings = UnifiedWindowSettings.Load();
            LoadWindowState();
        }

        private void LoadWindowState()
        {
            try
            {
                if (!_hasExternalSharedBounds)
                {
                    if (_settings.Width > 0 && _settings.Height > 0)
                    {
                        Width = _settings.Width;
                        Height = _settings.Height;
                    }

                    if (_settings.Left >= 0 && _settings.Top >= 0)
                    {
                        Left = _settings.Left;
                        Top = _settings.Top;
                    }
                    else
                    {
                        InitializeWindowPosition();
                    }

                    _normalLeft = Left;
                    _normalTop = Top;
                    _normalWidth = Width;
                    _normalHeight = Height;

                    if (_settings.IsMaximized)
                    {
                        Dispatcher.BeginInvoke(new Action(() => MaximizeWindow()), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                }

                if (LeftColumn != null && _settings.LeftColumnWidth > 0)
                {
                    LeftColumn.Width = new GridLength(_settings.LeftColumnWidth);
                }

                _isPinned = _settings.IsPinned;
                Topmost = _isPinned;
                Dispatcher.BeginInvoke(new Action(UpdatePinButtonState), System.Windows.Threading.DispatcherPriority.Loaded);

                if (DataContext is UnifiedWindowViewModel viewModel)
                {
                    viewModel.CurrentMode = UnifiedWindowViewModel.WindowMode.Writing;
                    UpdateModeButtonStyles(false);
                }

                TM.App.Log($"[UnifiedWindow] 窗口状态已加载 - 位置: ({Left}, {Top}), 大小: {Width}x{Height}, 模式: {_settings.CurrentMode}, Tab: {_settings.SelectedTabName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载窗口状态异常: {ex.Message}");
                InitializeWindowPosition();
                _normalLeft = Left;
                _normalTop = Top;
                _normalWidth = Width;
                _normalHeight = Height;
            }
        }
    }
}

