using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TM.Framework.Common.Controls.Markdown
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class MarkdownEditor : UserControl
    {
        private string _currentFilePath = string.Empty;

        public MarkdownEditor()
        {
            InitializeComponent();
            UpdateWordCount();
        }

        public void LoadContent(string markdown)
        {
            EditorTextBox.Text = markdown ?? string.Empty;
            UpdateWordCount();
        }

        public string GetContent()
        {
            return EditorTextBox.Text;
        }

        public void SetFilePath(string filePath)
        {
            _currentFilePath = filePath;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentFilePath))
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "Markdown文件 (*.md)|*.md|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                        DefaultExt = ".md",
                        FileName = "未命名.md"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        _currentFilePath = dialog.FileName;
                    }
                    else
                    {
                        return;
                    }
                }

                await File.WriteAllTextAsync(_currentFilePath, EditorTextBox.Text);

                GlobalToast.Success("保存成功", $"已保存到 {Path.GetFileName(_currentFilePath)}");
                App.Log($"[MarkdownEditor] 保存文件: {_currentFilePath}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("保存失败", ex.Message);
                App.Log($"[MarkdownEditor] 保存失败: {ex.Message}");
            }
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(EditorTextBox.Text))
                {
                    Clipboard.SetText(EditorTextBox.Text);
                    GlobalToast.Success("复制成功", "内容已复制到剪贴板");
                    App.Log("[MarkdownEditor] 内容已复制到剪贴板");
                }
                else
                {
                    GlobalToast.Warning("无内容", "编辑器内容为空");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("复制失败", ex.Message);
                App.Log($"[MarkdownEditor] 复制失败: {ex.Message}");
            }
        }

        private async void OnExport(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Markdown文件 (*.md)|*.md|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    DefaultExt = ".md",
                    FileName = "导出内容.md"
                };

                if (dialog.ShowDialog() == true)
                {
                    await File.WriteAllTextAsync(dialog.FileName, EditorTextBox.Text);
                    GlobalToast.Success("导出成功", $"已导出到 {Path.GetFileName(dialog.FileName)}");
                    App.Log($"[MarkdownEditor] 导出文件: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("导出失败", ex.Message);
                App.Log($"[MarkdownEditor] 导出失败: {ex.Message}");
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateWordCount();
        }

        private void UpdateWordCount()
        {
            try
            {
                if (EditorTextBox == null || WordCountText == null)
                    return;

                var text = EditorTextBox.Text;
                var wordCount = text.Count(c => !char.IsWhiteSpace(c));

                WordCountText.Text = wordCount.ToString();
            }
            catch (Exception ex)
            {
                App.Log($"[MarkdownEditor] 更新字数统计失败: {ex.Message}");
            }
        }
    }
}

