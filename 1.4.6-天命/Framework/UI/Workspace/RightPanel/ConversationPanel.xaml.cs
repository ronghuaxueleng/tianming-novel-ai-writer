using System;
using System.Reflection;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.UI.Workspace.Common.Controls;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Framework.UI.Workspace.RightPanel.Conversation;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using System.Windows.Media.Animation;

namespace TM.Framework.UI.Workspace.RightPanel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ConversationPanel : UserControl
    {
        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();
        private static readonly SolidColorBrush _referenceBlueBrush;

        static ConversationPanel()
        {
            _referenceBlueBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
            _referenceBlueBrush.Freeze();
        }

        private DispatcherTimer? _inputSyncTimer;
        private string _pendingInputText = string.Empty;
        private bool _isUpdatingInputBoxFromViewModel;
        private bool _containsReferenceInlines;

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

            _inputSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _inputSyncTimer.Tick += (_, _) => FlushPendingInputText();

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

                TodoOverlayPanel.StepRequested += (_, step) =>
                {
                    if (DataContext is SKConversationViewModel vm)
                    {
                        var text = !string.IsNullOrWhiteSpace(step.Description)
                            ? step.Description
                            : step.ToolName;

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            vm.InputText = text;
                        }

                        if (step.EventId != Guid.Empty)
                        {
                            var evt = vm.RunEvents.FirstOrDefault(e => e.Id == step.EventId);
                            if (evt != null)
                            {
                                vm.SelectedRunEvent = evt;
                            }
                        }
                    }
                };
            }

            InitializeReferenceDropdown();

            PanelComm.ClearMessageSelectionRequested += OnClearMessageSelectionRequested;
            Unloaded += OnConversationPanelUnloaded;

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
                ModelComboBox.Loaded += (_, _) =>
                {
                    ModelComboBox.ItemContainerGenerator.StatusChanged += (_, __) =>
                    {
                        if (ModelComboBox.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                        {
                            return;
                        }

                        for (int i = 0; i < ModelComboBox.Items.Count; i++)
                        {
                            if (ModelComboBox.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem item)
                            {
                                continue;
                            }

                            if (item.ContextMenu != null)
                            {
                                continue;
                            }

                            var menu = new ContextMenu
                            {
                                Padding = new Thickness(0)
                            };

                            var menuItem = new MenuItem
                            {
                                Header = "禁用此模型",
                                Padding = new Thickness(8, 0, 8, 0),
                                Height = 20,
                                FontSize = 11,
                                Tag = item.DataContext
                            };

                            menuItem.Click += OnDeleteModelClick;
                            menu.Items.Add(menuItem);
                            item.ContextMenu = menu;
                        }
                    };
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
                }, System.Windows.Threading.DispatcherPriority.Loaded);
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
                    Header = "➕ 新建会话"
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
                    Header = "📂 查看全部历史..."
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
                Width = 525,
                Height = 630,
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

            var icon = new Emoji.Wpf.TextBlock
            {
                Text = "📝",
                FontSize = 16,
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

            var icon = new Emoji.Wpf.TextBlock
            {
                Text = "⚙",
                FontSize = 16,
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
                AddMenuItem("📋 复制", vm.CopyMessageCommand);
                AddMenuItem("🔄 重新生成", vm.RegenerateAssistantMessageCommand);
                AddMenuItem("🗑 删除", vm.DeleteMessageCommand);
                AddMenuItem(message.IsStarred ? "★ 取消星标" : "☆ 星标", vm.ToggleStarCommand);
            }
            else if (message.IsUser)
            {
                AddMenuItem("📋 复制", vm.CopyMessageCommand);
                AddMenuItem("🔄 重新生成", vm.RegenerateFromUserMessageCommand);
                AddMenuItem("⟲ 撤回到输入框", vm.RecallToInputCommand);
                AddMenuItem("🗑 删除该轮（含回答）", vm.DeleteUserWithAssistantCommand);
                AddMenuItem(message.IsStarred ? "★ 取消星标" : "☆ 星标", vm.ToggleStarCommand);
            }
        }

        private void InputBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                if (DataContext is SKConversationViewModel vm &&
                    vm.SendCommand != null &&
                    vm.SendCommand.CanExecute(null))
                {
                    FlushPendingInputText(force: true);
                    vm.SendCommand.Execute(null);

                    ClearInputBox();
                    e.Handled = true;
                }
            }
        }

        private string GetInputBoxPlainText()
        {
            if (InputBox?.Document == null) return string.Empty;

            if (!_containsReferenceInlines)
            {
                var range = new TextRange(InputBox.Document.ContentStart, InputBox.Document.ContentEnd);
                return (range.Text ?? string.Empty).TrimEnd('\r', '\n').Trim();
            }

            var result = new System.Text.StringBuilder();
            foreach (var block in InputBox.Document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is InlineUIContainer container &&
                            container.Child is System.Windows.Controls.TextBlock tb &&
                            tb.Tag is string refText)
                        {
                            result.Append(refText);
                        }
                        else if (inline is Run run)
                        {
                            result.Append(run.Text);
                        }
                    }
                }
            }
            return result.ToString().Trim();
        }

        private void ClearInputBox()
        {
            if (InputBox?.Document == null) return;
            InputBox.Document.Blocks.Clear();
            InputBox.Document.Blocks.Add(new Paragraph());
            _containsReferenceInlines = false;
            UpdateInputPlaceholder();
        }

        private void UpdateInputPlaceholder()
        {
            if (InputPlaceholder == null) return;
            var hasContent = !string.IsNullOrWhiteSpace(_pendingInputText) || !string.IsNullOrWhiteSpace(GetInputBoxPlainText());
            InputPlaceholder.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnClearMessageSelectionRequested()
        {
            Dispatcher.InvokeAsync(ClearMessageSelection);
        }

        private void OnConversationPanelUnloaded(object sender, RoutedEventArgs e)
        {
            PanelComm.ClearMessageSelectionRequested -= OnClearMessageSelectionRequested;
            _inputSyncTimer?.Stop();
            _inputSyncTimer = null;
        }

        private void ClearMessageSelection()
        {
            if (DataContext is SKConversationViewModel vm)
            {
                MessagesListBox.SelectedItem = null;
                vm.SelectedMessage = null;
                vm.SelectedMessages.Clear();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "InputText" && sender is SKConversationViewModel vm)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isUpdatingInputBoxFromViewModel)
                    {
                        return;
                    }
                    var currentText = GetInputBoxPlainText();
                    if (currentText != vm.InputText)
                    {
                        _isUpdatingInputBoxFromViewModel = true;
                        SetInputBoxText(vm.InputText);
                        _isUpdatingInputBoxFromViewModel = false;
                    }
                });
            }
            else if (e.PropertyName == "Messages")
            {
                Dispatcher.InvokeAsync(UpdateEmptyStateVisibility);
            }
        }

        private void SetInputBoxText(string text)
        {
            if (InputBox == null) return;

            InputBox.Document.Blocks.Clear();
            var paragraph = new Paragraph(new Run(text ?? string.Empty));
            InputBox.Document.Blocks.Add(paragraph);
            _containsReferenceInlines = false;

            InputBox.CaretPosition = InputBox.Document.ContentEnd;

            _pendingInputText = text ?? string.Empty;
            UpdateInputPlaceholder();
        }

        private void OnContextUsageClick(object sender, MouseButtonEventArgs e)
        {
            ContextUsagePopup.IsOpen = !ContextUsagePopup.IsOpen;
        }

        private void OnContextUsageMouseEnter(object sender, MouseEventArgs e)
        {
            ContextUsagePopup.IsOpen = true;
        }

        private void OnContextUsageMouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void OnDeleteModelClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            if (menuItem.Tag is not TM.Services.Framework.AI.Core.UserConfiguration model)
            {
                return;
            }

            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var confirm = StandardDialog.ShowConfirm($"确定要禁用模型 \"{model.Name}\" 吗？\n禁用后可在模型管理中重新启用。", "禁用模型");
            if (confirm)
            {
                vm.DeleteModel(model);
            }
        }

        private void OnMessageBubbleRightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
            {
                return;
            }

            if (MessagesListBox.SelectedItem != item.DataContext)
            {
                MessagesListBox.SelectedItem = item.DataContext;
            }
        }
        private void TypingIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

        private void OnSessionMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            if (sender is MenuItem item && item.Tag is string sessionId)
            {
                _ = vm.SwitchSessionAsync(sessionId);
            }
        }

        private void MonitorButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            vm.ShowTodoOverlay = !vm.ShowTodoOverlay;
        }

        #region @引用下拉选择器

        private ReferenceDropdownViewModel? _referenceDropdownViewModel;

        private void InitializeReferenceDropdown()
        {
            if (ReferenceDropdownControl == null) return;

            _referenceDropdownViewModel = ServiceLocator.Get<ReferenceDropdownViewModel>();
            ReferenceDropdownControl.DataContext = _referenceDropdownViewModel;

            _referenceDropdownViewModel.ReferenceSelected += OnReferenceSelected;
        }

        private void InputBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not RichTextBox richTextBox) return;
            if (_referenceDropdownViewModel == null) return;

            if (_isUpdatingInputBoxFromViewModel)
            {
                return;
            }

            if (DataContext is SKConversationViewModel vm)
            {
                _pendingInputText = GetInputBoxPlainText();
                _inputSyncTimer?.Stop();
                _inputSyncTimer?.Start();
            }

            UpdateInputPlaceholder();

            var caretPosition = richTextBox.CaretPosition;
            var textBefore = caretPosition.GetTextInRun(LogicalDirection.Backward);

            if (!string.IsNullOrEmpty(textBefore) && textBefore.EndsWith("@"))
            {
                _referenceDropdownViewModel.Show(InputBox);
            }
        }

        private void OnReferenceSelected(string reference)
        {
            if (InputBox?.Document == null) return;

            _containsReferenceInlines = true;

            var caretPosition = InputBox.CaretPosition;

            var textBefore = caretPosition.GetTextInRun(LogicalDirection.Backward);
            if (!string.IsNullOrEmpty(textBefore) && textBefore.EndsWith("@"))
            {
                var start = caretPosition.GetPositionAtOffset(-1);
                if (start != null)
                {
                    var range = new TextRange(start, caretPosition);
                    range.Text = string.Empty;
                    caretPosition = start;
                }
            }

            var hyperlink = new Hyperlink(new Run(reference))
            {
                TextDecorations = null,
                Foreground = Brushes.White,
                Tag = reference
            };
            hyperlink.Background = _referenceBlueBrush;

            hyperlink.Click += (s, args) =>
            {
                if (s is Hyperlink hl && hl.Tag is string refText)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(refText, @"@(章节|chapter):(\S+)");
                    if (match.Success)
                    {
                        var chapterId = match.Groups[2].Value;
                        PanelComm
                            .RequestChapterNavigation(chapterId);
                    }
                }
            };

            var container = new InlineUIContainer(new System.Windows.Controls.TextBlock
            {
                Text = reference,
                Background = _referenceBlueBrush,
                Foreground = Brushes.White,
                FontSize = 13,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                Tag = reference,
                VerticalAlignment = VerticalAlignment.Center
            }, caretPosition);

            if (container.Child is System.Windows.Controls.TextBlock tb)
            {
                tb.MouseLeftButtonDown += (s, args) =>
                {
                    if (s is System.Windows.Controls.TextBlock block && block.Tag is string refText)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(refText, @"@(续写|continue|章节|chapter):(\S+)");
                        if (match.Success)
                        {
                            var chapterId = match.Groups[2].Value;
                            ServiceLocator.Get<PanelCommunicationService>()
                                .RequestChapterNavigation(chapterId);
                        }
                    }
                };
            }

            InputBox.CaretPosition = container.ElementEnd;

            var spaceRun = new Run(" ", InputBox.CaretPosition);
            InputBox.CaretPosition = spaceRun.ContentEnd;

            InputBox.Focus();
        }

        private void FlushPendingInputText(bool force = false)
        {
            if (!force)
            {
                _inputSyncTimer?.Stop();
            }

            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var text = _pendingInputText;
            if (vm.InputText != text)
            {
                vm.InputText = text;
            }
        }

        #endregion

        #region 空状态引导

        private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyStateVisibility();
        }

        private void UpdateEmptyStateVisibility()
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var hasMessages = vm.Messages.Count > 0;

            var hasHistorySessions = false;
            try
            {
                var sessions = vm.GetRecentSessions();
                hasHistorySessions = sessions.Count > 0;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(UpdateEmptyStateVisibility), ex);
            }

            var shouldHideGuide = hasMessages || hasHistorySessions || vm.HasDraftConversation;
            EmptyStateGuide.Visibility = shouldHideGuide ? Visibility.Collapsed : Visibility.Visible;
            MessagesListBox.Visibility = shouldHideGuide ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion
    }
}
