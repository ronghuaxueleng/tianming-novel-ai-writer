using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using TM.Framework.UI.Workspace.Common.Controls;
using TM.Framework.UI.Workspace.RightPanel.Conversation;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.RightPanel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ConversationPanel : UserControl
    {
        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();
        private static readonly SolidColorBrush _referenceBlueBrush;

        private static readonly System.Text.RegularExpressions.Regex ChapterRefRegex = new(
            @"@(章节|chapter):(\S+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex DirectiveRefRegex = new(
            @"@(续写|continue|章节|chapter):(\S+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        static ConversationPanel()
        {
            _referenceBlueBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
            _referenceBlueBrush.Freeze();
        }

        private bool _cachedHasHistorySessions;
        private DispatcherTimer? _inputSyncTimer;
        private string _pendingInputText = string.Empty;
        private bool _isUpdatingInputBoxFromViewModel;
        private bool _containsReferenceInlines;
        private bool _isSyncingQuickParams;
        private UIMessageItem? _streamingScrollTarget;
        private ScrollViewer? _messagesScrollViewer;
        private bool _scrollToBottomPending;
        private bool _userScrolledAway;
        private bool _isImeComposing;

        private UIStateCache? _uiStateCache;
        private UIStateCache UiStateCache => _uiStateCache ??= ServiceLocator.Get<UIStateCache>();
        private PanelCommunicationService? _panelComm;
        private PanelCommunicationService PanelComm => _panelComm ??= ServiceLocator.Get<PanelCommunicationService>();

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

            System.Diagnostics.Debug.WriteLine($"[ConversationPanel] {key}: {ex.Message}");
        }

        public ConversationPanel()
        {
            InitializeComponent();

            var uiCache = UiStateCache;
            if (uiCache.IsWarmedUp)
            {
                var shouldHideGuide = uiCache.HasHistorySessions;
                EmptyStateGuide.Visibility = shouldHideGuide ? Visibility.Collapsed : Visibility.Visible;
                MessagesListBox.Visibility = shouldHideGuide ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TodoOverlayPanel != null)
            {
                TodoOverlayPanel.CloseRequested += (_, _) =>
                {
                    if (DataContext is SKConversationViewModel vm)
                    {
                        vm.ShowTodoOverlay = false;
                    }
                };
            }

            InitializeReferenceDropdown();

            if (InputBox != null)
            {
                InputBox.AddHandler(
                    TextCompositionManager.TextInputStartEvent,
                    new TextCompositionEventHandler(OnInputBoxTextCompositionStart),
                    handledEventsToo: true);
                InputBox.AddHandler(
                    TextCompositionManager.TextInputEvent,
                    new TextCompositionEventHandler(OnInputBoxTextCompositionCompleted),
                    handledEventsToo: true);
                InputBox.LostFocus += OnInputBoxLostFocus_ImeReset;
            }

            PanelComm.ClearMessageSelectionRequested += OnClearMessageSelectionRequested;
            Unloaded += OnConversationPanelUnloaded;

            Loaded += (_, _) =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        if (InputBox == null) return;
                        InputBox.Focus();
                        System.Windows.Input.Keyboard.ClearFocus();
                    }
                    catch { }
                }));
            };

            this.DataContextChanged += (s, e) =>
            {
                if (e.OldValue is System.ComponentModel.INotifyPropertyChanged oldVm)
                {
                    oldVm.PropertyChanged -= OnViewModelPropertyChanged;
                }
                if (e.NewValue is System.ComponentModel.INotifyPropertyChanged newVm)
                {
                    newVm.PropertyChanged += OnViewModelPropertyChanged;
                }

                if (e.OldValue is SKConversationViewModel oldConvVm)
                {
                    oldConvVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                    oldConvVm.PropertyChanged -= OnViewModelPropertyChanged;
                }
                if (e.NewValue is SKConversationViewModel newConvVm)
                {
                    newConvVm.Messages.CollectionChanged += OnMessagesCollectionChanged;
                    newConvVm.PropertyChanged += OnViewModelPropertyChanged;
                    UpdateEmptyStateVisibility();
                }
            };

            if (ModelComboBox != null)
            {
                ModelComboBox.DropDownOpened += async (_, _) =>
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

                    for (int i = 0; i < ModelComboBox.Items.Count; i++)
                    {
                        if (ModelComboBox.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem item)
                            continue;

                        if (item.ContextMenu != null)
                            continue;

                        var menu = new ContextMenu { Padding = new Thickness(0) };

                        var menuItem = new MenuItem
                        {
                            Header = "禁用此模型",
                            Padding = new Thickness(8, 0, 8, 0),
                            Height = 20,
                            FontSize = 11
                        };
                        menuItem.Click += (s, e) =>
                        {
                            if (s is MenuItem mi &&
                                mi.Parent is ContextMenu cm &&
                                cm.PlacementTarget is ComboBoxItem ci)
                                OnDeleteModelClick(new MenuItem { Tag = ci.DataContext }, e);
                        };
                        menu.Items.Add(menuItem);

                        menu.Items.Add(new Separator { Margin = new Thickness(0, 2, 0, 2) });

                        var disableAllItem = new MenuItem
                        {
                            Header = "禁用全模型",
                            Padding = new Thickness(8, 0, 8, 0),
                            Height = 20,
                            FontSize = 11
                        };
                        disableAllItem.Click += OnDisableAllModelsClick;
                        menu.Items.Add(disableAllItem);

                        item.ContextMenu = menu;
                    }
                };
            }

        }

        private void OnModeCardClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border || border.Tag is not string modeStr)
                return;

            if (DataContext is not SKConversationViewModel vm)
                return;

            ChatMode mode;
            if (int.TryParse(modeStr, out var modeInt) && Enum.IsDefined(typeof(ChatMode), modeInt))
                mode = (ChatMode)modeInt;
            else if (Enum.TryParse<ChatMode>(modeStr, out mode)) { }
            else
                return;

            {
                vm.CurrentMode = mode;
                vm.EnterDraftConversation();
                TM.App.Log($"[ConversationPanel] 切换对话模式: {mode}");
                GlobalToast.Success("模式切换", $"已切换到 {mode} 模式");
                UpdateEmptyStateVisibility();

                Dispatcher.InvokeAsync(() =>
                {
                    InputBox?.Focus();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void OnSessionDropdownClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var sessions = vm.GetRecentSessions();

            SessionHistoryMenu.Items.Clear();

            foreach (var session in sessions)
            {
                var item = new MenuItem
                {
                    Header = $"{session.Title}",
                    Tag = session.Id,
                    ToolTip = session.UpdatedAt.ToString("MM-dd HH:mm")
                };
                item.Click += OnSessionMenuItemClick;
                SessionHistoryMenu.Items.Add(item);
            }

            if (sessions.Count > 0)
            {
                SessionHistoryMenu.Items.Add(new Separator());
            }

            if (vm.NewSessionCommand != null)
            {
                var newItem = new MenuItem
                {
                    Header = "新建会话"
                };
                newItem.Click += (_, _) =>
                {
                    if (vm.NewSessionCommand.CanExecute(null))
                    {
                        vm.NewSessionCommand.Execute(null);
                    }
                };
                SessionHistoryMenu.Items.Add(newItem);
            }

            if (vm.ShowHistoryCommand != null)
            {
                var viewAllItem = new MenuItem
                {
                    Header = "查看全部历史..."
                };
                viewAllItem.Click += (_, _) =>
                {
                    if (vm.ShowHistoryCommand.CanExecute(null))
                    {
                        vm.ShowHistoryCommand.Execute(null);
                    }
                };
                SessionHistoryMenu.Items.Add(viewAllItem);
            }

            if (sender is Button button)
            {
                SessionHistoryMenu.PlacementTarget = button;
                SessionHistoryMenu.Placement = PlacementMode.Bottom;
            }

            SessionHistoryMenu.IsOpen = true;
        }

        private void OnSessionTitleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                SessionTitleEditor.Visibility = Visibility.Visible;
                SessionTitleDisplay.Visibility = Visibility.Collapsed;

                SessionTitleEditor.Focus();
                SessionTitleEditor.SelectAll();

                e.Handled = true;
            }
        }

        private void FinishSessionTitleEdit(bool cancel)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            if (!cancel)
            {
                var newTitle = SessionTitleEditor.Text;
                vm.RenameCurrentSession(newTitle);
            }
            else
            {
                SessionTitleEditor.Text = vm.SessionTitle;
            }

            SessionTitleEditor.Visibility = Visibility.Collapsed;
            SessionTitleDisplay.Visibility = Visibility.Visible;
        }

        private void OnSessionTitleEditorLostFocus(object sender, RoutedEventArgs e)
        {
            FinishSessionTitleEdit(cancel: false);
        }

        private void OnSessionTitleEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FinishSessionTitleEdit(cancel: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                FinishSessionTitleEdit(cancel: true);
                e.Handled = true;
            }
        }

        private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void OnShowProjectSpecClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "项目写作规格",
                Width = 555,
                Height = 645,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false
            };

            StandardDialog.EnsureOwnerAndTopmost(dialog, Window.GetWindow(this));

            var chrome = new WindowChrome
            {
                CaptionHeight = 44,
                CornerRadius = new CornerRadius(12),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6)
            };
            WindowChrome.SetWindowChrome(dialog, chrome);

            var mainBorder = new Border
            {
                Style = (Style)FindResource("StandardDialogBorderStyle")
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Border
            {
                Style = (Style)FindResource("StandardDialogTitleBarStyle")
            };

            var titleGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };

            var icon = new Image
            {
                Source = TM.Framework.Common.Helpers.IconHelper.Get("Icon.Note"),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var title = new TextBlock
            {
                Text = "项目写作规格",
                Style = (Style)FindResource("StandardDialogTitleTextStyle")
            };

            titlePanel.Children.Add(icon);
            titlePanel.Children.Add(title);

            var closeBtn = new Button
            {
                Style = (Style)FindResource("StandardDialogCloseButtonStyle")
            };
            closeBtn.Click += (_, _) => dialog.Close();
            Grid.SetColumn(closeBtn, 1);

            titleGrid.Children.Add(titlePanel);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;

            var content = new ProjectSpecPanel { Margin = new Thickness(0) };

            if (content.DataContext is TM.Framework.UI.Workspace.Common.Controls.ProjectSpecPanelViewModel viewModel)
            {
                viewModel.SaveCompleted += () => dialog.Close();
                dialog.Closed += (_, _) => viewModel.Dispose();
            }

            Grid.SetRow(titleBar, 0);
            Grid.SetRow(content, 1);
            mainGrid.Children.Add(titleBar);
            mainGrid.Children.Add(content);

            mainBorder.Child = mainGrid;
            dialog.Content = mainBorder;
            StandardDialog.EnsureOwnerAndTopmost(dialog, dialog.Owner);
            dialog.ShowDialog();
        }

        private void OnShowGenerationParamsClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "生成参数",
                Width = 520,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false
            };

            StandardDialog.EnsureOwnerAndTopmost(dialog, Window.GetWindow(this));

            var chrome = new WindowChrome
            {
                CaptionHeight = 44,
                CornerRadius = new CornerRadius(12),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6)
            };
            WindowChrome.SetWindowChrome(dialog, chrome);

            var mainBorder = new Border
            {
                Style = (Style)FindResource("StandardDialogBorderStyle")
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Border
            {
                Style = (Style)FindResource("StandardDialogTitleBarStyle")
            };

            var titleGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };

            var icon = new Image
            {
                Source = TM.Framework.Common.Helpers.IconHelper.Get("Icon.Settings"),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var title = new TextBlock
            {
                Text = "生成参数",
                Style = (Style)FindResource("StandardDialogTitleTextStyle")
            };

            titlePanel.Children.Add(icon);
            titlePanel.Children.Add(title);

            var closeBtn = new Button
            {
                Style = (Style)FindResource("StandardDialogCloseButtonStyle")
            };
            closeBtn.Click += (_, _) => dialog.Close();
            Grid.SetColumn(closeBtn, 1);

            titleGrid.Children.Add(titlePanel);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;

            var content = new TM.Framework.UI.Workspace.Common.Controls.GenerationParamsPanel { Margin = new Thickness(0) };

            if (content.DataContext is TM.Framework.UI.Workspace.Common.Controls.GenerationParamsViewModel viewModel)
            {
                viewModel.SaveCompleted += () => dialog.Close();
            }

            Grid.SetRow(titleBar, 0);
            Grid.SetRow(content, 1);
            mainGrid.Children.Add(titleBar);
            mainGrid.Children.Add(content);

            mainBorder.Child = mainGrid;
            dialog.Content = mainBorder;
            StandardDialog.EnsureOwnerAndTopmost(dialog, dialog.Owner);
            dialog.ShowDialog();
        }

        private void OnMessagesSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            if (sender is not ListBox listBox)
            {
                return;
            }

            vm.SelectedMessages.Clear();

            foreach (var item in listBox.SelectedItems)
            {
                if (item is UIMessageItem msg)
                {
                    vm.SelectedMessages.Add(msg);
                }
            }
        }

        private void OnMessagesContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var menu = sender as ContextMenu ?? MessagesContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();

            var message = vm.SelectedMessage;
            if (message == null)
            {
                menu.IsOpen = false;
                return;
            }

            void AddMenuItem(string header, ICommand? command)
            {
                if (command == null || !command.CanExecute(null))
                {
                    return;
                }

                var item = new MenuItem
                {
                    Header = header
                };
                item.Click += (_, _) =>
                {
                    if (command.CanExecute(null))
                    {
                        command.Execute(null);
                    }
                };
                menu.Items.Add(item);
            }

            if (message.IsAssistant)
            {
                AddMenuItem("复制", vm.CopyMessageCommand);
                AddMenuItem("重新生成", vm.RegenerateAssistantMessageCommand);
                AddMenuItem("删除", vm.DeleteMessageCommand);
                AddMenuItem(message.IsStarred ? "取消星标" : "星标", vm.ToggleStarCommand);
            }
            else if (message.IsUser)
            {
                AddMenuItem("复制", vm.CopyMessageCommand);
                AddMenuItem("重新生成", vm.RegenerateFromUserMessageCommand);
                AddMenuItem("撤回到输入框", vm.RecallToInputCommand);
                AddMenuItem("删除该轮（含回答）", vm.DeleteUserWithAssistantCommand);
                AddMenuItem(message.IsStarred ? "取消星标" : "星标", vm.ToggleStarCommand);
            }
        }

    }
}

