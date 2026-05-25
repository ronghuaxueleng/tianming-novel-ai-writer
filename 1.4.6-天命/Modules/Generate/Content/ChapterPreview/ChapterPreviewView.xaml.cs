using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TM.Services.Modules.ProjectData.Models.Generate.Content;

namespace TM.Modules.Generate.Content.ChapterPreview
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ChapterPreviewView : UserControl
    {
        public ChapterPreviewView(ChapterPreviewViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                DataContext = viewModel;

                IsVisibleChanged += (s, e) =>
                {
                    if (e.NewValue is true && DataContext is ChapterPreviewViewModel vm)
                    {
                        vm.RefreshCommand.Execute(null);
                    }
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterPreviewView] 初始化失败: {ex.Message}");
                throw;
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ChapterPreviewViewModel vm && e.NewValue is ChapterTreeItem chapter)
            {
                vm.SelectChapterCommand.Execute(chapter);
            }
        }
    }
}
