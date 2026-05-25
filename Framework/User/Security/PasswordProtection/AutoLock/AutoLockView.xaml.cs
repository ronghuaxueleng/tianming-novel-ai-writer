using System;
using System.Reflection;
using System.Windows.Controls;

namespace TM.Framework.User.Security.PasswordProtection.AutoLock
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class AutoLockView : UserControl
    {
        public AutoLockView()
        {
            try
            {
                TM.App.Log("[AutoLockView] 开始初始化...");
                InitializeComponent();
                DataContext = ServiceLocator.Get<AutoLockViewModel>();

                Unloaded += (_, _) =>
                {
                    if (DataContext is IDisposable disposable)
                        disposable.Dispose();
                };

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

