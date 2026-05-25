using System;
using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.AIAssistant.MemoryManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class MemoryManagementView : UserControl
    {
        public MemoryManagementView(MemoryManagementViewModel viewModel)
        {
            try
            {
                InitializeComponent();
                DataContext = viewModel;
                TM.App.Log("[MemoryManagement] 记忆管理视图已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MemoryManagement] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}
