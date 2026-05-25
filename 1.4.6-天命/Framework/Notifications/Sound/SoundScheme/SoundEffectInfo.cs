namespace TM.Framework.Notifications.Sound.SoundScheme
{
    public class SoundEffectInfo
    {
        public string FileName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public string FileSizeText
        {
            get
            {
                if (FileSize < 1024)
                    return $"{FileSize} B";
                else if (FileSize < 1024 * 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                else
                    return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }

        public bool IsBuiltIn { get; set; } = true;
    }
}

