using System;

namespace TM.Framework.Common.Services
{
    public sealed class UIStateCache
    {
        public UIStateCache() { }

        #region 左栏状态

        public bool HasChaptersOrVolumes { get; private set; }

        public int ChapterCount { get; private set; }

        public int VolumeCount { get; private set; }

        #endregion

        #region 右栏状态

        public bool HasHistorySessions { get; private set; }

        public int SessionCount { get; private set; }

        #endregion

        #region 预热状态

        public bool IsWarmedUp { get; private set; }

        #endregion

        #region 设置方法

        public void SetChapterState(int volumeCount, int chapterCount)
        {
            VolumeCount = volumeCount;
            ChapterCount = chapterCount;
            HasChaptersOrVolumes = volumeCount > 0 || chapterCount > 0;
            TM.App.Log($"[UIStateCache] 左栏状态已缓存: 分类={volumeCount}, 章节={chapterCount}, 显示引导={!HasChaptersOrVolumes}");
        }

        public void SetSessionState(int sessionCount)
        {
            SessionCount = sessionCount;
            HasHistorySessions = sessionCount > 0;
            TM.App.Log($"[UIStateCache] 右栏状态已缓存: 会话数={sessionCount}, 显示引导={!HasHistorySessions}");
        }

        public void MarkWarmedUp()
        {
            IsWarmedUp = true;
            TM.App.Log("[UIStateCache] UI状态预热完成");
        }

        #endregion
    }
}
