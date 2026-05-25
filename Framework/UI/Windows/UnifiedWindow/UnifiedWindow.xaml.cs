using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TM.Framework.Appearance.Animation.ThemeTransition;
using TM.Framework.Common.ViewModels;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Windows
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class UnifiedWindow : Window, IModalOverlayHost
    {
        public void SetModalOverlay(bool visible)
        {
            if (ModalDimOverlay != null)
                ModalDimOverlay.IsBusy = visible;
        }

        public event Action? CreativeWindowRequested;

        private bool _hasStartedPreWarm;

        private bool _isMaximized = false;
        private bool _suppressTabChecked;
        private bool _hasExternalSharedBounds;

        public bool IsCustomMaximized => _isMaximized;

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

        private bool ConfirmAndCancelAIIfGenerating(string actionDesc)
        {
            if (!IsAICurrentlyGenerating()) return true;
            if (StandardDialog.ShowConfirm(
                $"AI正在生成中，{actionDesc}将中断本次生成。\n\n是否仍要切换？",
                "切换确认") != true)
            {
                return false;
            }
            CancelAIIfGenerating();
            return true;
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
        private double _normalWidth = 1400;
        private double _normalHeight = 1000;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private UnifiedWindowSettings _settings = new();
        private INotifyPropertyChanged? _trackedAIStateSource;

        private static Brush? _cachedOverlayTextBrush;
        public static void InvalidateOverlayBrushCache() => _cachedOverlayTextBrush = null;

        private static readonly CubicEase _easeIn = FreezeCubic(EasingMode.EaseIn);
        private static readonly CubicEase _easeOut = FreezeCubic(EasingMode.EaseOut);
        private static CubicEase FreezeCubic(EasingMode mode) { var e = new CubicEase { EasingMode = mode }; e.Freeze(); return e; }

        private UserControl? _activeView;
        private readonly HashSet<UserControl> _hostedViews = new();
        private bool _viewSwitchEnabled = true;
        private TimeSpan _switchInDuration = TimeSpan.FromMilliseconds(120);
        private Duration _fadeOutDuration = new(TimeSpan.FromMilliseconds(60));
        private ViewSwitchEffect _viewSwitchEffect = ViewSwitchEffect.Fade;

        private DateTime _lastSwitchAt = DateTime.MinValue;
        private static readonly TimeSpan _rapidSwitchThreshold = TimeSpan.FromMilliseconds(80);
        private DoubleAnimation? _fadeInAnimTemplate;
        private DoubleAnimation? _scaleInAnim;
        private DoubleAnimation? _tyInAnim;
        private DoubleAnimation? _txSlideInL;
        private DoubleAnimation? _txSlideInR;
        private DoubleAnimation? _tySlideUp;
        private DoubleAnimation? _tySlideDown;

        private void SwitchToView(UserControl? newView)
        {
            if (newView == null || newView == _activeView) return;

            EnsureViewHosted(newView);

            var oldView = _activeView;
            _activeView = newView;

            var now = DateTime.UtcNow;
            bool isRapidSwitch = (now - _lastSwitchAt) < _rapidSwitchThreshold;
            _lastSwitchAt = now;

            if (oldView != null)
            {
                oldView.BeginAnimation(UIElement.OpacityProperty, null);
                oldView.Opacity = 1;
                oldView.RenderTransform = null;

                if (!isRapidSwitch && _viewSwitchEnabled && _viewSwitchEffect != ViewSwitchEffect.None)
                {
                    var capturedOld = oldView;
                    var fadeOut = new DoubleAnimation(1, 0, _fadeOutDuration)
                    {
                        EasingFunction = _easeIn
                    };
                    fadeOut.Completed += (_, _) =>
                    {
                        if (ReferenceEquals(_activeView, capturedOld))
                        {
                            capturedOld.BeginAnimation(UIElement.OpacityProperty, null);
                            capturedOld.Opacity = 1;
                            return;
                        }
                        capturedOld.BeginAnimation(UIElement.OpacityProperty, null);
                        capturedOld.Opacity = 1;
                        capturedOld.Visibility = Visibility.Hidden;
                    };
                    fadeOut.Freeze();
                    oldView.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
                else
                {
                    oldView.Visibility = Visibility.Hidden;
                }
            }

            newView.BeginAnimation(UIElement.OpacityProperty, null);
            newView.Opacity = 1;
            newView.RenderTransform = null;
            newView.Visibility = Visibility.Visible;

            if (isRapidSwitch || !_viewSwitchEnabled || _viewSwitchEffect == ViewSwitchEffect.None)
                return;

            PlayNewViewAnimation(newView);
        }

        private void PlayNewViewAnimation(UserControl view)
        {
            var dur = new Duration(_switchInDuration);
            var ease = _easeOut;

            ScaleTransform? scale = null;
            TranslateTransform? translate = null;

            switch (_viewSwitchEffect)
            {
                case ViewSwitchEffect.FadeScale:
                    scale = new ScaleTransform(0.985, 0.985);
                    translate = new TranslateTransform(0, 8);
                    break;
                case ViewSwitchEffect.SlideUp:
                    translate = new TranslateTransform(0, 18);
                    break;
                case ViewSwitchEffect.SlideDown:
                    translate = new TranslateTransform(0, -18);
                    break;
                case ViewSwitchEffect.SlideLeft:
                    translate = new TranslateTransform(18, 0);
                    break;
                case ViewSwitchEffect.SlideRight:
                    translate = new TranslateTransform(-18, 0);
                    break;
            }

            if (scale != null || translate != null)
            {
                var group = new TransformGroup();
                group.Children.Add(scale ?? new ScaleTransform(1, 1));
                group.Children.Add(translate ?? new TranslateTransform(0, 0));
                view.RenderTransform = group;
                view.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            view.Opacity = 0;
            var fadeIn = (DoubleAnimation)_fadeInAnimTemplate!.Clone();
            fadeIn.Completed += (_, _) =>
            {
                view.BeginAnimation(UIElement.OpacityProperty, null);
                view.Opacity = 1;
                view.RenderTransform = null;
            };
            view.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, _scaleInAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, _scaleInAnim);
            }

            if (translate != null)
            {
                if (translate.X != 0)
                {
                    var txCached = translate.X == 18 ? _txSlideInL : translate.X == -18 ? _txSlideInR : null;
                    if (txCached != null)
                        translate.BeginAnimation(TranslateTransform.XProperty, txCached);
                    else
                    {
                        var txAnim = new DoubleAnimation(translate.X, 0, dur) { EasingFunction = ease };
                        txAnim.Freeze();
                        translate.BeginAnimation(TranslateTransform.XProperty, txAnim);
                    }
                }
                if (translate.Y != 0)
                {
                    var tyCached = translate.Y == 8 ? _tyInAnim : translate.Y == 18 ? _tySlideUp : translate.Y == -18 ? _tySlideDown : null;
                    if (tyCached != null)
                        translate.BeginAnimation(TranslateTransform.YProperty, tyCached);
                    else
                    {
                        var tyAnim = new DoubleAnimation(translate.Y, 0, dur) { EasingFunction = ease };
                        tyAnim.Freeze();
                        translate.BeginAnimation(TranslateTransform.YProperty, tyAnim);
                    }
                }
            }
        }

        private void RebuildAnimationCache()
        {
            var dur = new Duration(_switchInDuration);
            var ease = _easeOut;
            var fi = new DoubleAnimation(0, 1, dur) { EasingFunction = ease }; fi.Freeze(); _fadeInAnimTemplate = fi;
            var si = new DoubleAnimation(0.985, 1, dur) { EasingFunction = ease }; si.Freeze(); _scaleInAnim = si;
            var ty = new DoubleAnimation(8, 0, dur) { EasingFunction = ease }; ty.Freeze(); _tyInAnim = ty;
            var txL = new DoubleAnimation(18, 0, dur) { EasingFunction = ease }; txL.Freeze(); _txSlideInL = txL;
            var txR = new DoubleAnimation(-18, 0, dur) { EasingFunction = ease }; txR.Freeze(); _txSlideInR = txR;
            var tyU = new DoubleAnimation(18, 0, dur) { EasingFunction = ease }; tyU.Freeze(); _tySlideUp = tyU;
            var tyD = new DoubleAnimation(-18, 0, dur) { EasingFunction = ease }; tyD.Freeze(); _tySlideDown = tyD;
        }

        private void LoadViewSwitchSettings()
        {
            try
            {
                var settingsFile = StoragePathHelper.GetFilePath("Framework", "Appearance/Animation/ThemeTransition", "settings.json");
                AsyncSettingsLoader.LoadOrDefer<ThemeTransitionSettings>(settingsFile, s =>
                {
                    _viewSwitchEnabled = s.ViewSwitchEnabled;
                    _switchInDuration = TimeSpan.FromMilliseconds(s.ViewSwitchInMs);
                    _fadeOutDuration = new Duration(TimeSpan.FromMilliseconds(s.ViewSwitchOutMs));
                    _viewSwitchEffect = s.ViewSwitchEffect;
                    RebuildAnimationCache();
                }, "ViewSwitch_UnifiedWindow");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] 加载视图切换设置失败: {ex.Message}");
            }
        }

        private void EnsureViewHosted(UserControl newView)
        {
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

            newView.Visibility = Visibility.Hidden;
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

        public Rect GetSharedWindowBounds()
        {
            var left = _isMaximized ? _normalLeft : Left;
            var top = _isMaximized ? _normalTop : Top;
            var width = _isMaximized ? _normalWidth : (ActualWidth > 0 ? ActualWidth : Width);
            var height = _isMaximized ? _normalHeight : (ActualHeight > 0 ? ActualHeight : Height);
            return new Rect(left, top, width, height);
        }

        public void ApplySharedWindowBounds(double left, double top, double width, double height, bool isMaximized)
        {
            _hasExternalSharedBounds = true;
            _normalLeft = left;
            _normalTop = top;
            _normalWidth = width;
            _normalHeight = height;

            if (isMaximized)
            {
                if (_isMaximized)
                {
                    return;
                }

                Left = left;
                Top = top;
                Width = width;
                Height = height;
                MaximizeWindow();
                return;
            }

            if (_isMaximized)
            {
                RestoreWindow();
                return;
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public void StartPreWarm()
        {
            if (_hasStartedPreWarm)
            {
                return;
            }

            _hasStartedPreWarm = true;
            if (DataContext is UnifiedWindowViewModel viewModel)
            {
                _ = viewModel.PreWarmAllViewsAsync();
            }
        }

        public UnifiedWindow()
        {
            InitializeComponent();

            DataContext = ServiceLocator.Get<UnifiedWindowViewModel>();

            LoadViewSwitchSettings();
            RebuildAnimationCache();

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
            {
                SubscribeViewModelForOverlay(vm);
                vm.PropertyChanged += OnViewModelPropertyChangedForTree;
            }

            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is UnifiedWindowViewModel oldVm)
                {
                    UnsubscribeViewModelForOverlay(oldVm);
                    oldVm.PropertyChanged -= OnViewModelPropertyChangedForTree;
                }
                if (e.NewValue is UnifiedWindowViewModel newVm)
                {
                    _hasStartedPreWarm = false;
                    _hasPlayedInitialTreeAnimation = false;
                    SubscribeViewModelForOverlay(newVm);
                    newVm.PropertyChanged += OnViewModelPropertyChangedForTree;
                }
            };

            Closed += (_, __) =>
            {
                if (DataContext is UnifiedWindowViewModel vmToCancel)
                {
                    vmToCancel.Dispose();
                }
                CleanupOverlaySubscriptions();
            };
        }

        private bool _hasPlayedInitialTreeAnimation;

        private void OnViewModelPropertyChangedForTree(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(UnifiedWindowViewModel.TreeNodes)) return;

            if (_hasPlayedInitialTreeAnimation) return;
            _hasPlayedInitialTreeAnimation = true;
            PlayTreeAnimation();
        }

        private void PlayTreeAnimation()
        {
            var target = TreeViewBorder;
            if (target == null) return;

            target.BeginAnimation(UIElement.OpacityProperty, null);

            var translate = new TranslateTransform(0, 8);
            target.RenderTransform = translate;
            target.RenderTransformOrigin = new Point(0.5, 0.5);

            var dur = new Duration(_switchInDuration);
            var ease = _easeOut;

            var fadeIn = (DoubleAnimation)_fadeInAnimTemplate!.Clone();
            fadeIn.Completed += (_, _) =>
            {
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Opacity = 1;
                target.RenderTransform = null;
            };
            target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.YProperty, _tyInAnim);
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
            vm.PreWarmViewCallback = PreHostView;
            SwitchToView(vm.CurrentView);
            UpdateTrackedAIStateSource(vm.CurrentView);
        }

        private void UnsubscribeViewModelForOverlay(UnifiedWindowViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChangedForOverlay;
            vm.PreWarmViewCallback = null;
        }

        private void PreHostView(UserControl view)
        {
            try
            {
                if (ReferenceEquals(view.Parent, ViewHostPanel)) return;
                if (_hostedViews.Add(view))
                {
                    view.HorizontalAlignment = HorizontalAlignment.Stretch;
                    view.VerticalAlignment = VerticalAlignment.Stretch;
                }
                if (view.Parent is Panel oldPanel)
                    oldPanel.Children.Remove(view);
                view.Visibility = Visibility.Hidden;
                ViewHostPanel.Children.Add(view);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedWindow] PreHostView 失败: {ex.Message}");
            }
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
                Dispatcher.InvokeAsync(SyncAIGenerateOverlay, DispatcherPriority.Background);
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

    }
}

