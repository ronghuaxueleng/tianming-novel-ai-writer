using System.Collections.Generic;
using System.Reflection;
using System.Windows;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ChapterRepairDialog : Window
    {
        private readonly ChapterRepairViewModel _vm;

        public ChapterRepairDialog(string chapterId, string chapterTitle, List<ProblemItemDisplay> problems)
        {
            InitializeComponent();
            _vm = new ChapterRepairViewModel(chapterId, chapterTitle, problems);
            _vm.CloseRequested += () => Close();
            DataContext = _vm;
            Loaded += async (_, _) => await _vm.InitializeAsync();
            Closing += (_, _) =>
            {
                try
                {
                    if (_vm.CancelRepairCommand.CanExecute(null))
                        _vm.CancelRepairCommand.Execute(null);
                }
                catch
                {
                }
            };
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
