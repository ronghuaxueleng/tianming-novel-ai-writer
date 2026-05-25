using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    public partial class ChapterMarkdownEditor
    {
        public void SetContent(string content)
        {
            content ??= string.Empty;
            _viewModel.Content = content;
            _cachedEditorText = content;
            SetRichTextBoxText(content);
        }

        public string GetContent()
        {
            return _viewModel.Content;
        }

        public async Task<string?> LoadChapterContentAsync(ChapterInfo chapter)
        {
            try
            {
                var content = await _contentService.GetChapterAsync(chapter.Id);
                return content;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterMarkdownEditor] 加载章节失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
                return null;
            }
        }

        public void LoadTabContent(string id, string content, string originalContent)
        {
            _currentId = id;
            _originalContent = originalContent;
            _viewModel.Content = content;
            _viewModel.IsDirty = content != originalContent;
            SetRichTextBoxText(content);

            ResetEditorScrollToTop();

            TM.App.Log($"[ChapterMarkdownEditor] 切换到标签: {id}");
        }

        public void LoadNewContent(string chapterId, string title, string content)
        {
            _currentId = chapterId;
            _originalContent = string.Empty;
            _viewModel.Content = content;
            _viewModel.IsDirty = true;
            SetRichTextBoxText(content);

            ResetEditorScrollToTop();

            TM.App.Log($"[ChapterMarkdownEditor] 加载新生成内容: {chapterId}");
        }

        public void Clear()
        {
            _currentId = string.Empty;
            _originalContent = string.Empty;
            _viewModel.Content = string.Empty;
            _viewModel.IsDirty = false;
            SetRichTextBoxText("");
            ResetEditorScrollToTop();
        }

        private void ResetEditorScrollToTop()
        {
            if (EditorTextBox == null)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    EditorTextBox.ScrollToHome();
                    EditorTextBox.ScrollToVerticalOffset(0);

                    if (LineNumberScroller != null)
                    {
                        LineNumberScroller.ScrollToVerticalOffset(0);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce("ResetEditorScrollToTop", ex);
                }
            }));
        }

        public void SwitchToEditMode()
        {
            _viewModel.IsEditMode = true;
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentId)) return;
            if (_isSaving) return;
            _isSaving = true;

            try
            {
                var chapterId = _currentId;
                var content = GetContentForSave();
                var wasPolishFlow = _viewModel.IsPolishSplitMode || _viewModel.IsDiffMode;

                var _edParsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (_edParsed.HasValue && _edParsed.Value.chapterNumber == 1 && _edParsed.Value.volumeNumber > 1)
                {
                    var _edPrevVol = _edParsed.Value.volumeNumber - 1;
                    try
                    {
                        var _edArchiveStore = ServiceLocator.Get<VolumeFactArchiveStore>();
                        var _edPrevArchives = await _edArchiveStore.GetPreviousArchivesAsync(_edParsed.Value.volumeNumber);
                        if (!_edPrevArchives.Any(a => a.VolumeNumber == _edPrevVol))
                        {
                            TM.App.Log($"[ChapterEditor] 编辑器保存检测到新卷第1章，自动存档第{_edPrevVol}卷...");
                            var _edReconciler = ServiceLocator.Get<ConsistencyReconciler>();
                            await _edReconciler.AutoArchiveVolumeIfNeededAsync(_edPrevVol);
                        }
                    }
                    catch (Exception _edEx)
                    {
                        TM.App.Log($"[ChapterEditor] 第{_edParsed.Value.volumeNumber - 1}卷存檔检查失败（不阻断保存）: {_edEx.Message}");
                    }
                }

                var callback = ServiceLocator.Get<ContentGenerationCallback>();
                await callback.OnExternalContentSavedAsync(chapterId, content);

                var persisted = await _contentService.GetChapterAsync(chapterId) ?? content;

                _originalContent = persisted;
                if (!string.Equals(_viewModel.Content, persisted, StringComparison.Ordinal))
                {
                    _viewModel.Content = persisted;
                }
                _viewModel.IsDirty = false;
                _viewModel.StatusText = $"已保存 {DateTime.Now:HH:mm:ss}";
                if (wasPolishFlow)
                {
                    FinalizePolishSave(persisted);
                }
                else if (!string.Equals(persisted, content, StringComparison.Ordinal))
                {
                    SetRichTextBoxText(persisted);
                }
                ChapterSaved?.Invoke(chapterId, persisted);
                GlobalToast.Success("已保存", $"章节 {chapterId} 已保存");
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"保存失败: {ex.Message}";
                StandardDialog.ShowError($"保存失败：{ex.Message}", "保存失败");
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void OnContentChanged()
        {
            if (!string.IsNullOrEmpty(_currentId))
            {
                ContentModified?.Invoke(this, new ContentModifiedEventArgs
                {
                    Id = _currentId,
                    Content = _viewModel.Content
                });
            }
        }

        private void OnInlineEditClick(object sender, RoutedEventArgs e)
        {
            if (EditorTextBox?.Document == null)
            {
                return;
            }

            var selectedRange = new TextRange(EditorTextBox.Selection.Start, EditorTextBox.Selection.End);
            var selectedText = selectedRange.Text;

            var targetRange = string.IsNullOrWhiteSpace(selectedText)
                ? new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd)
                : selectedRange;

            selectedText = targetRange.Text;
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                GlobalToast.Warning("暂无内容", "当前章节没有可润色的文本");
                return;
            }

            var fullContentBeforeEdit = GetRichTextBoxText();
            var changesBlockToRestore = ExtractChangesBlock(fullContentBeforeEdit);

            InlineEditPopup.Show(
                selectedText,
                onAccept: (original, modified) =>
                {
                    if (_viewModel.IsDiffMode)
                    {
                        var acceptedContent = !string.IsNullOrEmpty(_pendingPolishSaveContent)
                            ? _pendingPolishSaveContent
                            : _viewModel.DiffModifiedContent;

                        if (changesBlockToRestore != null && FindChangesStartIndex(acceptedContent) < 0)
                        {
                            acceptedContent = acceptedContent.TrimEnd() + "\n\n" + changesBlockToRestore;
                        }

                        _viewModel.Content = acceptedContent;
                        _viewModel.IsDirty = acceptedContent != _originalContent;
                        TM.App.Log("[ChapterMarkdownEditor] 从对比模式接受润色结果，触发保存");
                        _ = SaveAsync();
                        return;
                    }

                    targetRange.Text = modified;

                    var newText = GetRichTextBoxText();
                    if (changesBlockToRestore != null && FindChangesStartIndex(newText) < 0)
                    {
                        newText = newText.TrimEnd() + "\n\n" + changesBlockToRestore;
                        var fullRange = new TextRange(EditorTextBox.Document.ContentStart, EditorTextBox.Document.ContentEnd);
                        fullRange.Text = newText;
                        TM.App.Log("[ChapterMarkdownEditor] 内联编辑：已补回原CHANGES块");
                    }

                    newText = GetRichTextBoxText();
                    _viewModel.Content = newText;
                    _viewModel.IsDirty = newText != _originalContent;

                    TM.App.Log("[ChapterMarkdownEditor] 应用内联编辑，触发保存");
                    _ = SaveAsync();
                },
                onShowDiff: (original, modified) =>
                {
                    var startOffset = new TextRange(EditorTextBox.Document.ContentStart, targetRange.Start).Text.Length;
                    var previewContent = MergePreviewContent(fullContentBeforeEdit, startOffset, original.Length, modified);
                    if (changesBlockToRestore != null && FindChangesStartIndex(previewContent) < 0)
                    {
                        previewContent = previewContent.TrimEnd() + "\n\n" + changesBlockToRestore;
                    }

                    SetPendingPolishComparison(fullContentBeforeEdit, previewContent);
                    RenderDiffInEditor(_viewModel.DiffOriginalContent, _viewModel.DiffModifiedContent);
                    InlineEditPopup.Hide();
                });
        }

        private void OnPolishButtonClick(object sender, RoutedEventArgs e)
        {
            InlineEditPopup.Visibility = System.Windows.Visibility.Visible;
        }

        private void RenderDiffInEditor(string original, string modified)
        {
            var diffBuilder = new DiffPlex.DiffBuilder.InlineDiffBuilder(new DiffPlex.Differ());
            var diff = diffBuilder.BuildDiffModel(original ?? "", modified ?? "");

            var doc = new FlowDocument
            {
                FontFamily = EditorTextBox.Document.FontFamily,
                FontSize = EditorTextBox.Document.FontSize,
                PagePadding = new Thickness(16, 8, 16, 8)
            };

            foreach (var line in diff.Lines)
            {
                var para = new Paragraph { Margin = new Thickness(0), LineHeight = 1.5 };
                switch (line.Type)
                {
                    case DiffPlex.DiffBuilder.Model.ChangeType.Inserted:
                        para.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x2E, 0xA0, 0x43));
                        para.Foreground = Brushes.Black;
                        break;
                    case DiffPlex.DiffBuilder.Model.ChangeType.Deleted:
                        para.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xD8, 0x32, 0x32));
                        para.Foreground = Brushes.Black;
                        break;
                    default:
                        para.Foreground = Brushes.Black;
                        break;
                }
                para.Inlines.Add(new Run(line.Text));
                doc.Blocks.Add(para);
            }

            _isUpdatingText = true;
            _viewModel.IsDiffMode = true;
            _viewModel.IsEditMode = true;
            EditorTextBox.Document = doc;
            EditorTextBox.IsReadOnly = true;
            _isUpdatingText = false;
        }

        private void ExitDiffMode()
        {
            _isUpdatingText = true;
            _viewModel.IsDiffMode = false;
            _viewModel.IsEditMode = true;
            EditorTextBox.IsReadOnly = false;
            _isUpdatingText = false;
            SetRichTextBoxText(_viewModel.Content);
        }

        private void SetPendingPolishComparison(string originalContent, string modifiedContent)
        {
            _polishOriginal = originalContent ?? string.Empty;
            _polishModified = modifiedContent ?? string.Empty;
            _pendingPolishSaveContent = _polishModified;
            _viewModel.DiffOriginalContent = _polishOriginal;
            _viewModel.DiffModifiedContent = _polishModified;
            _viewModel.IsPolishSplitMode = true;
        }

        private void ClearPendingPolishState()
        {
            _polishOriginal = string.Empty;
            _polishModified = string.Empty;
            _pendingPolishSaveContent = string.Empty;
            _viewModel.DiffOriginalContent = string.Empty;
            _viewModel.DiffModifiedContent = string.Empty;
            _viewModel.IsPolishSplitMode = false;
        }

        private string GetContentForSave()
        {
            if (!string.IsNullOrEmpty(_pendingPolishSaveContent))
                return _pendingPolishSaveContent;

            if (!string.IsNullOrEmpty(_viewModel.DiffModifiedContent))
                return _viewModel.DiffModifiedContent;

            return _viewModel.Content ?? string.Empty;
        }

        private static string MergePreviewContent(string fullContent, int startOffset, int originalLength, string modifiedContent)
        {
            var source = fullContent ?? string.Empty;
            var replacement = modifiedContent ?? string.Empty;
            var safeStart = Math.Max(0, Math.Min(startOffset, source.Length));
            var safeLength = Math.Max(0, Math.Min(originalLength, source.Length - safeStart));
            return source.Remove(safeStart, safeLength).Insert(safeStart, replacement);
        }

        private void FinalizePolishSave(string persistedContent)
        {
            EditorTextBox.IsReadOnly = false;
            _viewModel.IsDiffMode = false;
            _viewModel.IsEditMode = true;
            ClearPendingPolishState();
            SetRichTextBoxText(persistedContent);
        }

        public void SwitchToPolishSplitMode(string original, string modified)
        {
            SetPendingPolishComparison(original, modified);
            RenderDiffInEditor(original, modified);
        }

        public void ApplyInlineDiff(string original, string modified)
        {
            if (string.IsNullOrEmpty(original))
            {
                return;
            }

            var text = GetRichTextBoxText();
            var index = text.IndexOf(original, StringComparison.Ordinal);
            if (index < 0)
            {
                TM.App.Log("[ChapterMarkdownEditor] 未在当前内容中找到原文片段，无法应用 Diff");
                return;
            }

            var newText = text.Remove(index, original.Length).Insert(index, modified);
            SetRichTextBoxText(newText);

            _viewModel.Content = newText;
            _viewModel.IsDirty = newText != _originalContent;
        }
    }
}

