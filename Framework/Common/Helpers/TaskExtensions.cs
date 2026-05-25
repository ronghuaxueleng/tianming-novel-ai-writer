using System;
using System.Threading.Tasks;

namespace TM.Framework.Common.Helpers
{
    public static class TaskExtensions
    {
        public static async void SafeFireAndForget(this Task task, Action<Exception>? onError = null)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                if (onError != null)
                    onError.Invoke(ex);
                else
                    TM.App.Log($"[SafeFireAndForget] 未处理异常: {ex.Message}");
            }
        }
    }
}
