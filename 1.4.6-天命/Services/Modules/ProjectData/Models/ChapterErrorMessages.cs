namespace TM.Services.Modules.ProjectData.Models
{
    public static class ChapterErrorMessages
    {
        public const string SourceNotFound = 
            "来源章节 {0} 不存在";

        public const string VolumeNotFound = 
            "卷 {0} 不存在";

        public const string TargetAlreadyExists = 
            "目标章节 {0} 已存在，请使用 @重写:{0} 指令";

        public const string TargetNotFoundForRewrite = 
            "目标章节 {0} 不存在，请先生成该章节或检查章节ID";

        public const string DegradedGenerationWarning = 
            "已降级到仅MD/轻量生成，可能导致剧情不连贯";

        public const string InvalidChapterIdFormat = 
            "章节ID格式无效: {0}";
    }
}
