using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Framework.UI.Workspace.RightPanel.Conversation;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.RightPanel
{
    public partial class ConversationPanel
    {
        private void OnInputBoxTextCompositionStart(object sender, TextCompositionEventArgs e)
        {
            _isImeComposing = true;
        }

        private void OnInputBoxTextCompositionCompleted(object sender, TextCompositionEventArgs e)
        {
            _isImeComposing = false;
        }

        private void OnInputBoxLostFocus_ImeReset(object sender, RoutedEventArgs e)
        {
            _isImeComposing = false;
        }

        private void InputBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isImeComposing)
            {
                return;
            }

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;

                if (DataContext is SKConversationViewModel vm &&
                    vm.SendCommand != null &&
                    vm.SendCommand.CanExecute(null))
                {
                    FlushPendingInputText(force: true);
                    vm.SendCommand.Execute(null);

                    ClearInputBox();
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
            if (InputPlaceholder == null || InputBox?.Document == null) return;

            if (!string.IsNullOrEmpty(_pendingInputText))
            {
                InputPlaceholder.Visibility = Visibility.Collapsed;
                return;
            }

            var blocks = InputBox.Document.Blocks;
            bool isEmpty = blocks.Count == 0
                || (blocks.Count == 1
                    && blocks.FirstBlock is Paragraph p
                    && p.Inlines.Count == 0);

            InputPlaceholder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
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
            if (InputBox != null)
            {
                InputBox.RemoveHandler(
                    TextCompositionManager.TextInputStartEvent,
                    new TextCompositionEventHandler(OnInputBoxTextCompositionStart));
                InputBox.RemoveHandler(
                    TextCompositionManager.TextInputEvent,
                    new TextCompositionEventHandler(OnInputBoxTextCompositionCompleted));
                InputBox.LostFocus -= OnInputBoxLostFocus_ImeReset;
            }
            UnsubscribeStreamingScroll();
            if (_messagesScrollViewer != null)
            {
                _messagesScrollViewer.ScrollChanged -= OnMessagesScrollViewerScrollChanged;
                _messagesScrollViewer = null;
            }
            _scrollToBottomPending = false;
            _userScrolledAway = false;

            if (DataContext is System.IDisposable disposable)
                disposable.Dispose();
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
                RunOnUi(() => SyncInputBoxFromViewModel(vm));
            }
            else if (e.PropertyName == "Messages")
            {
                RunOnUi(UpdateEmptyStateVisibility);
            }
            else if (e.PropertyName == nameof(SKConversationViewModel.SessionTitle))
            {
                RunOnUi(UpdateEmptyStateVisibility);
            }
            else if (e.PropertyName == nameof(SKConversationViewModel.ActiveConfiguration))
            {
                RunOnUi(SyncQuickReasoningPopupValues);
            }
            else if (e.PropertyName == nameof(SKConversationViewModel.WritingEndpointConfigs))
            {
                if (QuickReasoningPopup.IsOpen)
                    RunOnUi(SyncQuickReasoningPopupValues);
            }
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.InvokeAsync(action);
            }
        }

        private void SyncInputBoxFromViewModel(SKConversationViewModel vm)
        {
            if (_isUpdatingInputBoxFromViewModel)
            {
                return;
            }

            var currentText = GetInputBoxPlainText();
            if (currentText == vm.InputText)
            {
                return;
            }

            _isUpdatingInputBoxFromViewModel = true;
            try
            {
                SetInputBoxText(vm.InputText);
            }
            finally
            {
                _isUpdatingInputBoxFromViewModel = false;
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

        private void OnQuickReasoningClick(object sender, MouseButtonEventArgs e)
        {
            if (!QuickReasoningPopup.IsOpen)
                SyncQuickReasoningPopupValues();
            QuickReasoningPopup.IsOpen = !QuickReasoningPopup.IsOpen;
        }

        private void OnQuickReasoningMouseEnter(object sender, MouseEventArgs e)
        {
            SyncQuickReasoningPopupValues();
            QuickReasoningPopup.IsOpen = true;
        }

        private void OnQuickReasoningMouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void OnModelComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QuickReasoningPopup.IsOpen)
                SyncQuickReasoningPopupValues();
        }

        private void SyncQuickReasoningPopupValues()
        {
            if (DataContext is not SKConversationViewModel vm) return;

            _isSyncingQuickParams = true;
            try
            {

                var endpointItems = vm.WritingEndpointConfigs;
                QuickTargetModelComboBox.ItemsSource = null;
                QuickTargetModelComboBox.ItemsSource = endpointItems;
                var selected = vm.QuickParamSelectedEndpoint;
                var selectedItem = selected != null
                    ? endpointItems.FirstOrDefault(x => x.Config.Id == selected.Config.Id)
                    : endpointItems.FirstOrDefault();
                QuickTargetModelComboBox.SelectedItem = selectedItem;
                vm.QuickParamSelectedEndpoint = selectedItem;

                var config = vm.QuickParamEffectiveConfig;

            }
            finally
            {
                _isSyncingQuickParams = false;
            }
        }

        private void OnQuickTargetModelChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingQuickParams) return;
            if (DataContext is not SKConversationViewModel vm) return;
            if (QuickTargetModelComboBox.SelectedItem is not WritingEndpointItem item) return;
            vm.QuickParamSelectedEndpoint = item;
            Dispatcher.InvokeAsync(SyncQuickReasoningPopupValues);
        }

        private void OnDisableAllModelsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var count = vm.ModelConfigurations.Count;
            if (count == 0)
            {
                GlobalToast.Info("无可用模型", "下拉列表中没有已启用的模型");
                return;
            }

            var confirm = StandardDialog.ShowConfirm($"确定要禁用当前列表中全部 {count} 个模型吗？\n禁用后可在模型管理中逐个重新启用。", "全部禁用");
            if (confirm)
            {
                vm.DisableAllModels();
            }
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

            if (DataContext is SKConversationViewModel)
            {
                if (_inputSyncTimer == null)
                {
                    _inputSyncTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
                    _inputSyncTimer.Tick += (_, _) => FlushPendingInputText();
                }
                _inputSyncTimer.Stop();
                _inputSyncTimer.Start();
            }

            UpdateInputPlaceholder();

            if (_isImeComposing)
            {
                return;
            }

            var caretPosition = richTextBox.CaretPosition;
            var textBefore = caretPosition.GetTextInRun(LogicalDirection.Backward);

            if (!string.IsNullOrEmpty(textBefore) && textBefore.EndsWith('@'))
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
            if (!string.IsNullOrEmpty(textBefore) && textBefore.EndsWith('@'))
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
                    var match = ChapterRefRegex.Match(refText);
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
                        var match = DirectiveRefRegex.Match(refText);
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
            _inputSyncTimer?.Stop();

            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var text = GetInputBoxPlainText();
            _pendingInputText = text;

            if (vm.InputText != text)
            {
                vm.InputText = text;
            }
        }

        #endregion

        #region 空状态引导

        private void OnMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (DataContext is not SKConversationViewModel vm) return;
            var shouldHideGuide = vm.Messages.Count > 0 || _cachedHasHistorySessions || vm.HasDraftConversation;
            EmptyStateGuide.Visibility = shouldHideGuide ? Visibility.Collapsed : Visibility.Visible;
            MessagesListBox.Visibility = shouldHideGuide ? Visibility.Visible : Visibility.Collapsed;

            var action = e.Action;
            if ((action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ||
                 action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                && MessagesListBox.Items.Count > 0)
            {
                QueueScrollMessagesToBottom();
            }

            if (action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is UIMessageItem msg && msg.IsStreaming)
                    {
                        UnsubscribeStreamingScroll();
                        _streamingScrollTarget = msg;
                        msg.PropertyChanged += OnStreamingMessagePropertyChanged;
                    }
                }
            }
        }

        private void OnStreamingMessagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not UIMessageItem msg) return;

            if (e.PropertyName == nameof(UIMessageItem.Content))
            {
                if (!_userScrolledAway)
                    QueueScrollMessagesToBottom();
            }
            else if (e.PropertyName == nameof(UIMessageItem.IsStreaming) && !msg.IsStreaming)
            {
                _userScrolledAway = false;
                QueueScrollMessagesToBottom();
                UnsubscribeStreamingScroll();
            }
        }

        private void UnsubscribeStreamingScroll()
        {
            if (_streamingScrollTarget != null)
            {
                _streamingScrollTarget.PropertyChanged -= OnStreamingMessagePropertyChanged;
                _streamingScrollTarget = null;
            }
        }

        private void ScrollMessagesToBottom()
        {
            if (MessagesListBox.Items.Count == 0) return;

            if (_messagesScrollViewer == null)
            {
                _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessagesListBox);
                if (_messagesScrollViewer != null)
                    _messagesScrollViewer.ScrollChanged += OnMessagesScrollViewerScrollChanged;
            }

            if (_userScrolledAway && _streamingScrollTarget != null) return;

            MessagesListBox.ScrollIntoView(MessagesListBox.Items[^1]);
            _messagesScrollViewer?.ScrollToEnd();
        }

        private void OnMessagesScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_messagesScrollViewer == null || _streamingScrollTarget == null) return;

            var distanceToBottom = _messagesScrollViewer.ScrollableHeight - _messagesScrollViewer.VerticalOffset;

            if (distanceToBottom > 50)
            {
                _userScrolledAway = true;
            }
            else if (_userScrolledAway && distanceToBottom <= 20)
            {
                _userScrolledAway = false;
            }
        }

        private void QueueScrollMessagesToBottom()
        {
            if (_scrollToBottomPending) return;
            _scrollToBottomPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _scrollToBottomPending = false;
                ScrollMessagesToBottom();
            }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void UpdateEmptyStateVisibility()
        {
            if (DataContext is not SKConversationViewModel vm)
            {
                return;
            }

            var hasMessages = vm.Messages.Count > 0;

            try
            {
                var sessions = vm.GetRecentSessions();
                _cachedHasHistorySessions = sessions.Count > 0;
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(UpdateEmptyStateVisibility), ex);
            }

            var shouldHideGuide = hasMessages || _cachedHasHistorySessions || vm.HasDraftConversation;
            EmptyStateGuide.Visibility = shouldHideGuide ? Visibility.Collapsed : Visibility.Visible;
            MessagesListBox.Visibility = shouldHideGuide ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion
    }
}

