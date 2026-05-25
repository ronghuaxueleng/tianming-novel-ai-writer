using System;
using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class VersionTestingView : UserControl
{
    public VersionTestingView(VersionTestingViewModel viewModel)
    {
        try
        {
            InitializeComponent();
            DataContext = viewModel;
            TM.App.Log("[VersionTesting] 提示词版本测试视图已加载");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTesting] 视图初始化失败: {ex.Message}");
        }
    }
}
