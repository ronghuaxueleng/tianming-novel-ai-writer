using System.Collections.Generic;

namespace TM.Framework.Common.Helpers.AI;

public static class ModuleMetadataRegistry
{
    public record ModuleMetadata(
        string[] OutputFields,
        string[] InputVariables,
        string? Description = null,
        string? ModuleType = null
    );

    private static readonly Dictionary<string, ModuleMetadata> _registry = new()
    {
        ["拆书分析师"] = new(
            OutputFields: new[]
            {
                "世界构建手法", "力量体系设计", "环境描写技巧", "势力设计技巧", "世界观亮点",
                "主角塑造手法", "配角设计技巧", "人物关系设计", "金手指设计", "角色塑造亮点",
                "情节结构技巧", "冲突设计手法", "高潮布局技巧", "伏笔技巧", "剧情设计亮点"
            },
            InputVariables: new[] { "书名", "作者", "类型" },
            Description: "拆书分析：提取写作技巧/世界观/角色/剧情方法",
            ModuleType: "design"
        ),
        ["素材设计师"] = new(
            OutputFields: new[]
            {
                "整体构思",
                "世界观素材-构建手法", "世界观素材-力量体系", "世界观素材-环境描写", "世界观素材-势力设计", "世界观素材-亮点",
                "角色素材-主角塑造", "角色素材-配角设计", "角色素材-人物关系", "角色素材-金手指", "角色素材-角色亮点",
                "剧情素材-情节结构", "剧情素材-冲突设计", "剧情素材-高潮布局", "剧情素材-伏笔设计", "剧情素材-剧情亮点"
            },
            InputVariables: new[] { "素材名称", "题材类型", "来源拆书" },
            Description: "基于拆书分析生成三维度创作素材",
            ModuleType: "design"
        ),
        ["小说设计师"] = new(
            OutputFields: System.Array.Empty<string>(),
            InputVariables: new[] { "规则名称" },
            Description: "小说设计：世界观/角色/势力/位置/剧情等规则设计",
            ModuleType: "design"
        ),
        ["小说创作者"] = new(
            OutputFields: System.Array.Empty<string>(),
            InputVariables: new[] { "大纲名称", "章节标题", "场景标题" },
            Description: "小说创作：大纲/章节/蓝图等生成",
            ModuleType: "generate"
        ),

        ["世界规则"] = new(
            OutputFields: new[] { "规则描述", "限制条件", "应用示例" },
            InputVariables: new[] { "规则名称" },
            Description: "设计世界观基础规则",
            ModuleType: "design"
        ),
        ["地理环境"] = new(
            OutputFields: new[] { "地点描述", "气候特征", "资源分布" },
            InputVariables: new[] { "地点名称" },
            Description: "设计地理环境",
            ModuleType: "design"
        ),
        ["势力组织"] = new(
            OutputFields: new[] { "势力目的", "组织结构", "势力历史" },
            InputVariables: new[] { "势力名称" },
            Description: "设计势力组织",
            ModuleType: "design"
        ),
        ["历史文化"] = new(
            OutputFields: new[] { "历史描述", "时间节点", "历史影响" },
            InputVariables: new[] { "历史名称" },
            Description: "设计历史文化",
            ModuleType: "design"
        ),

        ["角色档案"] = new(
            OutputFields: new[] { "外貌特征", "背景故事", "性格特点" },
            InputVariables: new[] { "角色名称" },
            Description: "设计角色基础档案",
            ModuleType: "design"
        ),
        ["能力体系"] = new(
            OutputFields: new[] { "能力列表", "天赋特长", "成长逻辑" },
            InputVariables: new[] { "能力名称" },
            Description: "设计角色能力体系",
            ModuleType: "design"
        ),
        ["关系网络"] = new(
            OutputFields: new[] { "关系网络", "核心动机", "内心冲突" },
            InputVariables: new[] { "关系名称" },
            Description: "设计角色关系网络",
            ModuleType: "design"
        ),

        ["核心主题"] = new(
            OutputFields: new[] { "核心思想", "情感基调", "传达信息", "象征元素" },
            InputVariables: new[] { "主题名称" },
            Description: "设计故事核心主题",
            ModuleType: "design"
        ),
        ["冲突设计"] = new(
            OutputFields: new[] { "冲突起源", "发展过程", "解决方案" },
            InputVariables: new[] { "冲突名称" },
            Description: "设计故事冲突",
            ModuleType: "design"
        ),
        ["情节结构"] = new(
            OutputFields: new[] { "剧情概要", "关键情节点", "支线剧情" },
            InputVariables: new[] { "结构名称" },
            Description: "设计情节结构",
            ModuleType: "design"
        ),
        ["伏笔线索"] = new(
            OutputFields: new[] { "伏笔描述", "埋设位置", "揭示计划" },
            InputVariables: new[] { "伏笔名称" },
            Description: "设计伏笔线索",
            ModuleType: "design"
        ),

        ["故事框架"] = new(
            OutputFields: new[] { "核心概念", "主题思想", "核心冲突" },
            InputVariables: new[] { "框架名称" },
            Description: "构建故事整体框架",
            ModuleType: "generate"
        ),
        ["卷级规划"] = new(
            OutputFields: new[] { "时代背景", "卷级主题", "核心事件" },
            InputVariables: new[] { "卷名称" },
            Description: "规划卷级内容",
            ModuleType: "generate"
        ),
        ["结局规划"] = new(
            OutputFields: new[] { "主线结局", "支线收束", "角色归宿", "伏笔回收", "主题呼应" },
            InputVariables: new[] { "结局名称", "结局类型" },
            Description: "设计故事结局",
            ModuleType: "generate"
        ),

        ["章节拆分"] = new(
            OutputFields: new[] { "章节分组", "拆分说明" },
            InputVariables: new[] { "拆分名称" },
            Description: "拆分章节结构",
            ModuleType: "generate"
        ),
        ["剧情分配"] = new(
            OutputFields: new[] { "关键故事弧", "指令说明" },
            InputVariables: new[] { "分配名称" },
            Description: "分配剧情内容",
            ModuleType: "generate"
        ),
        ["节奏平衡"] = new(
            OutputFields: new[] { "节奏变化", "高潮分布", "峰值构建" },
            InputVariables: new[] { "平衡名称", "章节范围" },
            Description: "设计节奏平衡",
            ModuleType: "generate"
        ),

        ["章节概要"] = new(
            OutputFields: new[] { "核心内容", "章节摘要" },
            InputVariables: new[] { "概要名称" },
            Description: "生成章节概要",
            ModuleType: "generate"
        ),
        ["关键要素"] = new(
            OutputFields: new[] { "悬念钩子", "情感冲击", "转折设计" },
            InputVariables: new[] { "要素名称", "关联章节" },
            Description: "设计关键要素",
            ModuleType: "generate"
        ),
        ["关联管理"] = new(
            OutputFields: new[] { "章节关联", "伏笔回收", "关系走向" },
            InputVariables: new[] { "关联名称", "关联章节" },
            Description: "管理章节关联",
            ModuleType: "generate"
        ),

        ["场景规划"] = new(
            OutputFields: new[] { "开篇", "发展", "转折", "结尾" },
            InputVariables: new[] { "场景名称" },
            Description: "规划场景内容",
            ModuleType: "generate"
        ),
        ["细节设计"] = new(
            OutputFields: new[] { "象征意象", "潜台词设计", "卷主题连接" },
            InputVariables: new[] { "设计名称" },
            Description: "设计场景细节",
            ModuleType: "generate"
        ),
        ["要素整合"] = new(
            OutputFields: new[] { "角色发展", "剧情推进", "主题表达" },
            InputVariables: new[] { "整合名称", "关联章节" },
            Description: "整合章节要素",
            ModuleType: "generate"
        ),

        ["世界观一致性"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称", "校验目标" },
            Description: "校验世界观一致性",
            ModuleType: "validate"
        ),
        ["时空背景校验"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称", "时间线描述", "空间描述" },
            Description: "校验时空背景",
            ModuleType: "validate"
        ),
        ["实体资格审查"] = new(
            OutputFields: new[] { "审查结果", "问题描述", "修复建议" },
            InputVariables: new[] { "审查名称", "实体类型", "实体名称" },
            Description: "审查实体资格",
            ModuleType: "validate"
        ),

        ["因果链闭环"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验因果链闭环",
            ModuleType: "validate"
        ),
        ["角色行为逻辑"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验角色行为逻辑",
            ModuleType: "validate"
        ),
        ["时序一致性"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称", "时间描述" },
            Description: "校验时序一致性",
            ModuleType: "validate"
        ),

        ["伏笔系统校验"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验伏笔系统",
            ModuleType: "validate"
        ),
        ["叙事层级保真"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验叙事层级",
            ModuleType: "validate"
        ),
        ["节奏平衡检查"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "检查节奏平衡",
            ModuleType: "validate"
        ),

        ["格式合规检查"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "检查格式合规",
            ModuleType: "validate"
        ),
        ["引用关系验证"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "验证引用关系",
            ModuleType: "validate"
        ),
        ["版本兼容性"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "检查版本兼容性",
            ModuleType: "validate"
        ),

        ["文风一致性"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验文风一致性",
            ModuleType: "validate"
        ),
        ["载体DNA验证"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "验证载体DNA",
            ModuleType: "validate"
        ),
        ["输出格式校验"] = new(
            OutputFields: new[] { "校验结果", "问题描述", "修复建议" },
            InputVariables: new[] { "校验名称" },
            Description: "校验输出格式",
            ModuleType: "validate"
        ),

        ["Spec"] = new(
            OutputFields: new[] { "写作风格", "叙述视角", "情感基调", "目标字数", "段落长度", "对话比例", "必须包含", "必须避免" },
            InputVariables: new[] { "类型名称" },
            Description: "定义创作规格约束，控制生成内容的风格和格式",
            ModuleType: "spec"
        ),

        ["AIGC"] = new(
            OutputFields: new[] { "修改后文本" },
            InputVariables: System.Array.Empty<string>(),
            Description: "内联编辑和文本润色，直接输出修改后的文本",
            ModuleType: "aigc"
        ),
    };

    public static ModuleMetadata? GetMetadata(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return null;

        return _registry.TryGetValue(categoryName, out var metadata) ? metadata : null;
    }

    public static bool IsRegistered(string categoryName)
    {
        return !string.IsNullOrWhiteSpace(categoryName) && _registry.ContainsKey(categoryName);
    }

    public static IEnumerable<string> GetAllCategories() => _registry.Keys;
}
