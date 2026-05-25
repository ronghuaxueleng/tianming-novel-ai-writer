using System;
using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Security.PasswordProtection.AutoLock
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class AutoLockView : UserControl
    {
        public AutoLockView()
        {
            try
            {
                TM.App.Log("[AutoLockView] 开始初始化...");
                InitializeComponent();
                DataContext = ServiceLocator.Get<AutoLockViewModel>();
                TM.App.Log("[AutoLockView] 初始化完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AutoLockView] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}

