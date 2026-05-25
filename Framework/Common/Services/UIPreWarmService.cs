using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TM.Framework.Common.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public static class UIPreWarmService
    {
        private static bool _dialogsAndControlsPreWarmed;
        private static bool _preJitDone;

        public static void PreWarmDialogsAndControls()
        {
            if (_dialogsAndControlsPreWarmed) return;
            _dialogsAndControlsPreWarmed = true;

            try
            {
                TryCreate(() => new Controls.Dialogs.StandardDialog());
                TryCreate(() => new Controls.Dialogs.BatchGenerationDialog());
                Controls.Feedback.ToastNotification.PreWarm();
                TryCreate(() => new Controls.Dialogs.ColorPickerDialog(System.Windows.Media.Colors.White));
                TryCreate(() => new TM.Modules.Generate.Content.Views.PackageHistoryDialog());
                TryCreate(() => new TM.Framework.UI.Workspace.RightPanel.Dialogs.SessionHistoryDialog());
                TryCreate(() => new TM.Framework.Appearance.AutoTheme.TimeBased.TimeScheduleEditDialog());
                TryCreate(() => new TM.Framework.User.Profile.BasicInfo.AvatarUploadDialog());
                TryCreate(() => new TM.Framework.User.Account.Login.AccountRenewDialog());

                var rtbWarm = new RichTextBox { Visibility = Visibility.Collapsed };
                rtbWarm.Measure(new Size(200, 50));

                TryCreateAndMeasure(() => new Controls.DataManagement.FunctionalDetailForm());

                TryCreateAndMeasure(() => new Controls.DataManagement.TwoColumnEditorLayout());
                TryCreateAndMeasure(() => new Controls.EmojiPicker());
                TryCreateAndMeasure(() => new Search.SearchTextBox());
                TryCreateAndMeasure(() => new Controls.Markdown.MarkdownStreamViewer());

                TryCreateAndMeasure(() => new Controls.Feedback.BusyOverlay());
                TryCreateAndMeasure(() => new Controls.Feedback.LoadingIndicator());

                var tb = new TextBox();
                if (Application.Current.TryFindResource("StandardTextBoxStyle") is Style tbStyle)
                    tb.Style = tbStyle;
                tb.Visibility = Visibility.Collapsed;
                tb.Text = "prewarm";
                tb.Measure(new Size(200, 36));
                tb.Text = "";

                var cb = new ComboBox();
                if (Application.Current.TryFindResource("StandardComboBoxStyle") is Style cbStyle)
                    cb.Style = cbStyle;
                cb.Visibility = Visibility.Collapsed;
                cb.Items.Add("prewarm");
                cb.Measure(new Size(200, 36));
                cb.Items.Clear();
                PreWarmTreeComboBoxTemplate();

                var pb = new PasswordBox();
                if (Application.Current.TryFindResource("StandardPasswordBoxStyle") is Style pbStyle)
                    pb.Style = pbStyle;
                pb.Visibility = Visibility.Collapsed;
                pb.Measure(new Size(200, 36));

                TM.App.Log("[UIPreWarm] 弹窗/控件/模板预热完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] 预热异常（忽略）: {ex.Message}");
            }
        }

        public static async Task PreJitCriticalTypesAsync(CancellationToken cancellationToken = default)
        {
            if (_preJitDone) return;
            _preJitDone = true;

            try
            {
                var typesToPreJit = CollectCriticalTypes();
                int totalMethods = 0;
                const int batchSize = 8;
                int index = 0;

                while (index < typesToPreJit.Count)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    var end = Math.Min(index + batchSize, typesToPreJit.Count);
                    var batchStart = index;
                    index = end;

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        for (int i = batchStart; i < end; i++)
                            totalMethods += PreJitType(typesToPreJit[i]);
                    }, DispatcherPriority.ApplicationIdle);
                }

                TM.App.Log($"[UIPreWarm] Pre-JIT 完成: {typesToPreJit.Count} 类型, {totalMethods} 方法");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] Pre-JIT 异常（忽略）: {ex.Message}");
            }
        }

        private static int PreJitType(Type type)
        {
            int count = 0;
            try
            {
                var methods = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                foreach (var method in methods)
                {
                    try
                    {
                        if (!method.IsAbstract && !method.ContainsGenericParameters)
                        {
                            RuntimeHelpers.PrepareMethod(method.MethodHandle);
                            count++;
                        }
                    }
                    catch { }
                }

                var ctors = type.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                foreach (var ctor in ctors)
                {
                    try
                    {
                        RuntimeHelpers.PrepareMethod(ctor.MethodHandle);
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        private static List<Type> CollectCriticalTypes()
        {
            var types = new List<Type>();
            var assembly = typeof(UIPreWarmService).Assembly;

            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    try
                    {
                        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                            continue;

                        if (typeof(System.Windows.Data.IValueConverter).IsAssignableFrom(type) ||
                            typeof(System.Windows.Data.IMultiValueConverter).IsAssignableFrom(type))
                        {
                            types.Add(type);
                            continue;
                        }

                        if (typeof(INotifyPropertyChanged).IsAssignableFrom(type) &&
                            !typeof(UIElement).IsAssignableFrom(type))
                        {
                            types.Add(type);
                            continue;
                        }

                        if (typeof(UIElement).IsAssignableFrom(type))
                        {
                            var declaredMethods = type.GetMethods(
                                BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.DeclaredOnly);
                            if (declaredMethods.Length >= 10)
                            {
                                types.Add(type);
                            }
                            continue;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] 类型扫描异常: {ex.Message}");
            }

            return types;
        }

        private static void PreWarmTreeComboBoxTemplate()
        {
            try
            {
                var comboBox = new ComboBox
                {
                    Width = 240,
                    Height = 36,
                    Visibility = Visibility.Collapsed,
                    ItemsSource = new List<TM.Framework.Common.Controls.TreeNodeItem>
                    {
                        new()
                        {
                            Name = "主页导航",
                            Icon = IconHelper.TryGet("Icon.Home"),
                            Level = 1,
                            Children = new TM.Framework.Common.ViewModels.RangeObservableCollection<TM.Framework.Common.Controls.TreeNodeItem>
                            {
                                new()
                                {
                                    Name = "示例分类",
                                    Icon = IconHelper.TryGet("Icon.Folder"),
                                    Level = 2
                                }
                            }
                        }
                    }
                };

                if (Application.Current.TryFindResource("TreeComboBoxStyle") is Style treeComboStyle)
                    comboBox.Style = treeComboStyle;

                comboBox.Measure(new Size(240, 36));
                comboBox.Arrange(new Rect(0, 0, 240, 36));
                comboBox.ApplyTemplate();
                comboBox.UpdateLayout();
                comboBox.IsDropDownOpen = true;
                comboBox.ApplyTemplate();

                if (comboBox.Template?.FindName("PART_Popup", comboBox) is System.Windows.Controls.Primitives.Popup popup &&
                    popup.Child is FrameworkElement popupChild)
                {
                    popupChild.Measure(new Size(240, 400));
                    popupChild.Arrange(new Rect(0, 0, 240, 400));
                    popupChild.UpdateLayout();
                }

                comboBox.IsDropDownOpen = false;
                comboBox.ItemsSource = null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] 树形下拉模板预热跳过: {ex.Message}");
            }
        }

        private static void TryCreate(Func<object> factory)
        {
            try { _ = factory(); }
            catch { }
        }

        private static void TryCreateAndMeasure(Func<UserControl> factory)
        {
            try
            {
                var control = factory();
                control.Visibility = Visibility.Collapsed;
                control.Measure(new Size(800, 600));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] 控件预热跳过: {ex.Message}");
            }
        }

        private static readonly Dictionary<Type, UserControl> _preCreatedViews = new();
        private static bool _viewPreWarmStarted;
        public static UserControl? TakePreCreatedView(Type viewType)
        {
            if (_preCreatedViews.Remove(viewType, out var view))
                return view;
            return null;
        }
        public static void ClearPreCreatedViews()
        {
            _preCreatedViews.Clear();
            _viewPreWarmStarted = false;
        }
        public static async Task PreWarmPriorityViewsAsync()
        {
            if (_viewPreWarmStarted) return;
            _viewPreWarmStarted = true;

            try
            {
                var priorityModules = new[]
                {
                    TM.Framework.Common.Constants.NavigationDefinitions.Design,
                    TM.Framework.Common.Constants.NavigationDefinitions.Generate,
                    TM.Framework.Common.Constants.NavigationDefinitions.SmartAssistant,
                };
                var skipTypes = new HashSet<Type>
                {
                    typeof(TM.Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView),
                };

                foreach (var module in priorityModules)
                {
                    foreach (var sub in module.SubModules)
                    {
                        foreach (var func in sub.Functions)
                        {
                            var viewType = func.ViewType;
                            if (skipTypes.Contains(viewType)) continue;
                            if (_preCreatedViews.ContainsKey(viewType)) continue;

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    if (_preCreatedViews.ContainsKey(viewType)) return;
                                    var view = CreateViewInstance(viewType);
                                    if (view != null)
                                        _preCreatedViews[viewType] = view;
                                }
                                catch (Exception ex)
                                {
                                    TM.App.Log($"[UIPreWarm] 视图预创建跳过: {viewType.Name} - {ex.Message}");
                                }
                            }, DispatcherPriority.Background);
                        }
                    }
                }

                TM.App.Log($"[UIPreWarm] 优先视图预创建完成，共 {_preCreatedViews.Count} 个");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UIPreWarm] 优先视图预创建异常: {ex.Message}");
            }
        }

        private static UserControl? CreateViewInstance(Type viewType)
        {
            var view = ServiceLocator.GetOrDefault(viewType) as UserControl;
            if (view != null) return view;

            var viewFactory = ServiceLocator.GetOrDefault(typeof(Factories.IViewFactory)) as Factories.IViewFactory;
            if (viewFactory != null)
                return viewFactory.CreateView(viewType);

            return Activator.CreateInstance(viewType) as UserControl;
        }
    }
}
