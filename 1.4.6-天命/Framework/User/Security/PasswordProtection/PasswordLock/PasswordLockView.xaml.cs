using System;
using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Security.PasswordProtection.PasswordLock
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class PasswordLockView : UserControl
    {
        public PasswordLockView()
        {
            try
            {
                TM.App.Log("[PasswordLockView] 开始初始化...");
                InitializeComponent();
                DataContext = ServiceLocator.Get<PasswordLockViewModel>();
                TM.App.Log("[PasswordLockView] 初始化完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PasswordLockView] 初始化失败: {ex.Message}");
                throw;
            }
        }
    }
}

