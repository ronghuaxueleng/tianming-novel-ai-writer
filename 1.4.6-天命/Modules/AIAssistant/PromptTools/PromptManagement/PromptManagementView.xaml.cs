using System;
using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class PromptManagementView : UserControl
{
    public PromptManagementView(PromptManagementViewModel viewModel)
    {
        try
        {
            InitializeComponent();
            DataContext = viewModel;
            TM.App.Log("[PromptManagement] 提示词管理视图已加载");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 初始化失败: {ex.Message}");
            throw;
        }
    }
}
