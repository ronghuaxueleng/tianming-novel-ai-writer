using System.Windows;
using System.Windows.Media;

namespace TM.Framework.Common.Helpers
{
    public static class IconHelper
    {
        public static ImageSource Get(string key)
        {
            return (ImageSource)Application.Current.FindResource(key);
        }

        public static ImageSource? TryGet(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return Application.Current.TryFindResource(key) as ImageSource;
        }
    }
}
