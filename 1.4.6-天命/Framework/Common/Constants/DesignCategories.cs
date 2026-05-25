namespace TM.Framework.Common.Constants
{
    public static class DesignCategories
    {
        #region 智能拆书 + 创作模板（2类）

        public const string CreativeMaterials = "CreativeMaterials";

        public const string BookAnalysis = "BookAnalysis";

        #endregion

        #region 世界观设计（4类）

        public const string WorldRules = "WorldRules";

        public const string Geography = "Geography";

        public const string Factions = "Factions";

        public const string History = "History";

        #endregion

        #region 角色设计（3类）

        public const string Profiles = "Profiles";

        public const string Abilities = "Abilities";

        public const string Relationships = "Relationships";

        #endregion

        #region 剧情设计（4类）

        public const string Theme = "Theme";

        public const string Conflicts = "Conflicts";

        public const string Structure = "Structure";

        public const string Foreshadowing = "Foreshadowing";

        #endregion

        public static readonly string[] All = new[]
        {
            CreativeMaterials, BookAnalysis,
            WorldRules, Geography, Factions, History,
            Profiles, Abilities, Relationships,
            Theme, Conflicts, Structure, Foreshadowing
        };

        public static string GetDisplayName(string category)
        {
            return category switch
            {
                CreativeMaterials => "创作目录",
                BookAnalysis => "拆书目录",
                WorldRules => "世界规则",
                Geography => "地理环境",
                Factions => "势力组织",
                History => "历史文化",
                Profiles => "角色档案",
                Abilities => "能力体系",
                Relationships => "关系网络",
                Theme => "核心主题",
                Conflicts => "冲突设计",
                Structure => "情节结构",
                Foreshadowing => "伏笔线索",
                _ => category
            };
        }
    }
}
