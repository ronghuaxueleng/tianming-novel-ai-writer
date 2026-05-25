namespace TM.Framework.Common.Helpers
{
    public static class GlobalToast
    {
        public static void Success(string title, string message = "", int duration = 3000)
        {
            ToastNotification.ShowSuccess(title, message, duration);
        }

        public static void Warning(string title, string message = "", int duration = 3000)
        {
            ToastNotification.ShowWarning(title, message, duration);
        }

        public static void Error(string title, string message = "", int duration = 4000)
        {
            ToastNotification.ShowError(title, message, duration);
        }

        public static void Info(string title, string message = "", int duration = 3000)
        {
            ToastNotification.ShowInfo(title, message, duration);
        }
    }
}

