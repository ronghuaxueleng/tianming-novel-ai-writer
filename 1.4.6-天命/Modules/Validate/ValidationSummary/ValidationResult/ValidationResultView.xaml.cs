using System;
using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Validate.ValidationSummary.ValidationResult
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ValidationResultView : UserControl
    {
        public ValidationResultView(ValidationResultViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                DataContext = viewModel;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationResultView] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}
