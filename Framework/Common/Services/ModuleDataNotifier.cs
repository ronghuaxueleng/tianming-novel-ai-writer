using System;

namespace TM.Framework.Common.Services
{
    public static class ModuleDataNotifier
    {
        public static event Action? DataSaved;

        internal static void RaiseDataSaved()
        {
            try
            {
                DataSaved?.Invoke();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ModuleDataNotifier] 订阅者处理异常，不影响保存流程: {ex.Message}");
            }
        }
    }
}
