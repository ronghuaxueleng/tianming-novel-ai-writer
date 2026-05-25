using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Framework.Common.Helpers.AI;

public class PromptGenerationService : IPromptGenerationService
{
    private readonly IAITextGenerationService _aiTextGenerationService;

    public PromptGenerationService(AIService aiService)
    {
        _aiTextGenerationService = aiService;
    }

    public async Task<PromptGenerationResult> GenerateModulePromptAsync(PromptGenerationContext context)
    {
        var result = new PromptGenerationResult();

        try
        {
            if (string.IsNullOrWhiteSpace(context.ModuleKey) && string.IsNullOrWhiteSpace(context.ModuleDisplayName))
            {
                result.Success = false;
                result.ErrorMessage = "缺少模块标识或名称";
                return result;
            }

            var metaPrompt = BuildMetaPrompt(context);
            var aiResult = await _aiTextGenerationService.GenerateAsync(metaPrompt);

            if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
            {
                result.Success = false;
                result.ErrorMessage = string.IsNullOrWhiteSpace(aiResult.ErrorMessage)
                    ? "AI未返回有效内容"
                    : aiResult.ErrorMessage;
                return result;
            }

            var raw = aiResult.Content.Trim();

            try
            {
                if (TryExtractJsonObject(raw, out var jsonText))
                {
                    using var document = JsonDocument.Parse(jsonText);
                    var root = document.RootElement;

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("content", out var contentProp) &&
                            contentProp.ValueKind == JsonValueKind.String)
                        {
                            result.Content = contentProp.GetString() ?? string.Empty;
                        }

                        if (string.IsNullOrWhiteSpace(result.Content))
                        {
                            throw new JsonException("content missing");
                        }

                        if (root.TryGetProperty("name", out var nameProp) &&
                            nameProp.ValueKind == JsonValueKind.String)
                        {
                            result.Name = nameProp.GetString();
                        }

                        if (root.TryGetProperty("description", out var descProp) &&
                            descProp.ValueKind == JsonValueKind.String)
                        {
                            result.Description = descProp.GetString();
                        }

                        if (root.TryGetProperty("tags", out var tagsProp))
                        {
                            var tags = new System.Collections.Generic.List<string>();

                            if (tagsProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in tagsProp.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.String)
                                    {
                                        var t = item.GetString();
                                        if (!string.IsNullOrWhiteSpace(t))
                                        {
                                            tags.Add(t.Trim());
                                        }
                                    }
                                }
                            }
                            else if (tagsProp.ValueKind == JsonValueKind.String)
                            {
                                var rawTags = tagsProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawTags))
                                {
                                    foreach (var part in rawTags.Split(new[] { ',', '，' }))
                                    {
                                        var t = part.Trim();
                                        if (!string.IsNullOrWhiteSpace(t))
                                        {
                                            tags.Add(t);
                                        }
                                    }
                                }
                            }

                            if (tags.Count > 0)
                            {
                                result.Tags = tags;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(result.Description))
                        {
                            result.Description = context.ExtraRequirement;
                        }

                        result.Success = true;
                        return result;
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                TM.App.Log($"[PromptGenerationService] JSON解析失败，回退纯文本: {jsonEx.Message}");
            }

            result.Success = true;
            result.Content = raw;
            result.Description = context.ExtraRequirement;
            return result;
        }
        catch (System.Exception ex)
        {
            TM.App.Log($"[PromptGenerationService] 生成提示词失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static string BuildMetaPrompt(PromptGenerationContext context)
    {
        return (context.PromptRootCategory ?? string.Empty) switch
        {
            "统一提示词" => BuildUnifiedMetaPrompt(context),
            "Spec" => BuildSpecMetaPrompt(context),
            "校验" => BuildValidateMetaPrompt(context),
            "AIGC" => BuildAIGCMetaPrompt(context),
            _ => BuildBusinessMetaPrompt(context)
        };
    }

    private static bool TryExtractJsonObject(string text, out string jsonText)
    {
        jsonText = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return false;

        jsonText = text.Substring(start, end - start + 1);
        return true;
    }

    private static string BuildUnifiedMetaPrompt(PromptGenerationContext context)
    {
        return context.PromptSubCategory switch
        {
            "拆书分析师" => BuildUnifiedBookAnalystMetaPrompt(context),
            "素材设计师" => BuildUnifiedMaterialDesignerMetaPrompt(context),
            "小说设计师" => BuildUnifiedNovelDesignerMetaPrompt(context),
            "小说创作者" => BuildUnifiedNovelCreatorMetaPrompt(context),
            _ => BuildUnifiedGenericMetaPrompt(context)
        };
    }

    private static string BuildUnifiedBookAnalystMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<role>Expert prompt engineer for novel writing software. Task: write a System Prompt for 统一提示词/拆书分析师 (write to JSON 'content' field), usable directly in business runtime.</role>");
        sb.AppendLine("请严格参考并复刻内置模板的写法与结构，要求生成结果稳定、可落库、可被字段契约解析。");
        sb.AppendLine();
        sb.AppendLine("<placeholder_rules mandatory=\"true\">");
        sb.AppendLine("System Prompt 只允许使用以下占位符：{书名} {作者} {类型}。不得出现任何其他占位符（包括 {上下文数据}，上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("</placeholder_rules>");
        sb.AppendLine();
        sb.AppendLine("<content_quality_rules mandatory=\"true\">");
        sb.AppendLine("1) 所有分析必须以系统上下文中提供的书籍原文节选为唯一依据来源；不得脱离上下文臆测设定或编造细节。");
        sb.AppendLine("2) 结论要具体、可执行、可复用：优先输出写作方法/技巧/策略/注意事项，而不是空泛评价。");
        sb.AppendLine("3) 允许引用原文或上下文片段作为依据（如上下文提供），但不得虚构引用。");
        sb.AppendLine("4) 若上下文信息不足以支持某结论，必须明确说明信息不足，并指出需要补充的数据类型（不要猜测）。");
        sb.AppendLine("5) 输出应尽量覆盖：写作技法、世界观、人物塑造、情节结构等维度，避免只集中在单一角度。");
        sb.AppendLine("6) 每个字段输出 200-500 字的深度分析。");
        sb.AppendLine("</content_quality_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_protocol mandatory=\"true\">");
        sb.AppendLine("System Prompt（content）中不得写入字段清单原文，也不得规定具体 JSON 示例。");
        sb.AppendLine("但必须在最后一行包含：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("（运行时系统会在此之后追加具体字段清单与格式契约）。");
        sb.AppendLine("</output_protocol>");
        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("content 必须包含并按顺序组织为：");
        sb.AppendLine("1) 第一行：你是一位资深的网文拆书分析师。 （不要使用提示词工程师口吻）");
        sb.AppendLine("2) 当前分析目标段：书名/作者/类型，分别引用 {书名}{作者}{类型}");
        sb.AppendLine("3) 核心任务段：从作品中提炼可迁移的写作方法论，覆盖世界观、角色、剧情三大维度");
        sb.AppendLine("4) 分析要求段：结合系统上下文中提供的书籍原文节选进行分析；提炼「为什么这样写有效」和「如何在新作中复用」；输出可直接作为新作创作参考的方法论，而非复述原书情节");
        sb.AppendLine("5) 最后一行：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</content_structure>");
        sb.AppendLine();
        sb.AppendLine("<layout_rules mandatory=\"true\">");
        sb.AppendLine("每个段落之间必须空一行，避免所有内容挤在一起。");
        sb.AppendLine("</layout_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildUnifiedMaterialDesignerMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<role>Expert prompt engineer for novel writing software. Task: write a System Prompt for 统一提示词/素材设计师 (write to JSON 'content' field), usable directly in business runtime.</role>");
        sb.AppendLine("请严格参考并复刻内置模板的写法与结构，要求生成结果稳定、可落库、可被字段契约解析。");
        sb.AppendLine();
        sb.AppendLine("<placeholder_rules mandatory=\"true\">");
        sb.AppendLine("System Prompt 只允许使用以下占位符：{素材名称} {题材类型} {来源拆书}。不得出现任何其他占位符（包括 {上下文数据}，上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("</placeholder_rules>");
        sb.AppendLine();
        sb.AppendLine("<content_quality_rules mandatory=\"true\">");
        sb.AppendLine("1) 最高优先级：必须严格遵守系统上下文中的「创作规格约束」，包括题材锚点、必须包含的元素和必须避免的元素。所有设计内容必须完全符合指定题材的风格和世界观基调，绝不允许偏离题材。");
        sb.AppendLine("2) 素材必须可直接用于 {题材类型}：明确用途、适用场景与触发条件，避免抽象描述。");
        sb.AppendLine("3) 从拆书分析中借鉴「写作技法和叙事结构」（如节奏控制、冲突模型、伏笔手法），但故事设定（世界观、力量体系、角色身份、环境元素）必须完全原创且严格属于目标题材，不得照搬原书的具体设定。");
        sb.AppendLine("4) 若上下文不足以生成可靠素材，必须说明缺口并提出需要补充的内容（不要猜测）。");
        sb.AppendLine("5) 每个字段输出 200-500 字的原创设计内容。");
        sb.AppendLine("</content_quality_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_protocol mandatory=\"true\">");
        sb.AppendLine("System Prompt（content）中不得写入字段清单原文，也不得规定具体 JSON 示例。");
        sb.AppendLine("但必须在最后一行包含：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("（运行时系统会在此之后追加具体字段清单与格式契约）。");
        sb.AppendLine("</output_protocol>");
        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("content 必须包含并按顺序组织为：");
        sb.AppendLine("1) 第一行：你是一位专业的网文创作素材设计师。 （不要使用提示词工程师口吻）");
        sb.AppendLine("2) 当前设计目标段：素材名称/题材类型/来源拆书，分别引用 {素材名称}{题材类型}{来源拆书}");
        sb.AppendLine("3) 核心任务段：基于系统上下文中提供的「创作规格约束」和拆书分析数据，为新小说设计原创的三维度创作素材（世界观/角色/剧情）");
        sb.AppendLine("4) 设计要求段：最高优先级严格遵守创作规格约束；首先给出整体构思；借鉴拆书的写作技法但设定完全原创；素材之间保持内在逻辑关联");
        sb.AppendLine("5) 最后一行：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</content_structure>");
        sb.AppendLine();
        sb.AppendLine("<layout_rules mandatory=\"true\">");
        sb.AppendLine("每个段落之间必须空一行，避免所有内容挤在一起。");
        sb.AppendLine("</layout_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildUnifiedNovelDesignerMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<role>Expert prompt engineer for novel writing software. Task: write a System Prompt for 统一提示词/小说设计师 (write to JSON 'content' field), usable directly in business runtime.</role>");
        sb.AppendLine("请严格参考并复刻内置模板的写法与结构，要求生成结果稳定、可直接用于落库与渲染，并能被字段契约解析。");
        sb.AppendLine();
        sb.AppendLine("<placeholder_rules mandatory=\"true\">");
        sb.AppendLine("System Prompt 只允许使用以下占位符：{规则名称}。不得出现任何其他占位符（包括 {上下文数据}，上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("</placeholder_rules>");
        sb.AppendLine();
        sb.AppendLine("<content_quality_rules mandatory=\"true\">");
        sb.AppendLine("1) 设计内容必须与系统上下文中已有设定保持一致；不得引入与上下文冲突的新设定。");
        sb.AppendLine("2) 输出必须可直接落库与渲染：结构清晰、措辞明确、避免空泛与口号式描述。");
        sb.AppendLine("3) 必须进行一致性自检：若发现与上下文存在冲突/缺口，需要显式指出冲突点，并给出兼容方案或需要补充的信息。");
        sb.AppendLine("4) 信息不足时不得猜测，需说明缺口并提出需要补充的数据类型。");
        sb.AppendLine("5) 每个字段输出 200-600 字，内容具体且可直接用于创作。");
        sb.AppendLine("</content_quality_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_protocol mandatory=\"true\">");
        sb.AppendLine("System Prompt（content）中不得写入字段清单原文，也不得规定具体 JSON 示例。");
        sb.AppendLine("但必须在最后一行包含：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("（运行时系统会在此之后追加具体字段清单与格式契约）。");
        sb.AppendLine("</output_protocol>");
        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("content 必须包含并按顺序组织为：");
        sb.AppendLine("1) 第一行：你是一位专业的网文小说设计师。 （不要使用提示词工程师口吻）");
        sb.AppendLine("2) 当前设计目标段：引用 {规则名称}");
        sb.AppendLine("3) 服务说明段：服务于设计模块（世界观规则/角色规则/势力规则/位置规则/剧情规则），根据系统上下文中提供的已有设定和当前模块的字段结构，为指定规则生成高质量的设计内容");
        sb.AppendLine("4) 设计要求段：参考系统上下文中的素材数据、已有规则和世界观设定；与上下文中已有的设计数据保持一致性，不出现逻辑矛盾；新设计要有独特性和记忆点，避免通用模板化内容");
        sb.AppendLine("5) 最后一行：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</content_structure>");
        sb.AppendLine();
        sb.AppendLine("<layout_rules mandatory=\"true\">");
        sb.AppendLine("每个段落之间必须空一行，避免所有内容挤在一起。");
        sb.AppendLine("</layout_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildUnifiedNovelCreatorMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<role>Expert prompt engineer for novel writing software. Task: write a System Prompt for 统一提示词/小说创作者 (write to JSON 'content' field), usable directly in business runtime.</role>");
        sb.AppendLine("请严格参考并复刻内置模板的写法与结构，要求生成结果稳定、可直接用于落库，并能被字段契约解析。");
        sb.AppendLine();
        sb.AppendLine("<placeholder_rules mandatory=\"true\">");
        sb.AppendLine("System Prompt 只允许使用以下占位符：{大纲名称} {章节标题} {场景标题}。不得出现任何其他占位符（包括 {上下文数据}，上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("</placeholder_rules>");
        sb.AppendLine();
        sb.AppendLine("<content_quality_rules mandatory=\"true\">");
        sb.AppendLine("1) 创作内容必须与系统上下文中的设计数据一致，保持人物动机、时间线、因果关系与设定连续性。");
        sb.AppendLine("2) 目标是可直接落库：语句完整、表达自然、避免提纲式空话与泛泛而谈。");
        sb.AppendLine("3) 必须先做一致性自检：若上下文存在冲突或缺口，需要指出并给出兼容处理（或说明需要补充的信息），不得臆测硬编。");
        sb.AppendLine("4) 每个字段输出 200-800 字，内容具体且可直接用于写作。");
        sb.AppendLine("</content_quality_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_protocol mandatory=\"true\">");
        sb.AppendLine("System Prompt（content）中不得写入字段清单原文，也不得规定具体 JSON 示例。");
        sb.AppendLine("但必须在最后一行包含：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("（运行时系统会在此之后追加具体字段清单与格式契约）。");
        sb.AppendLine("</output_protocol>");
        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("content 必须包含并按顺序组织为：");
        sb.AppendLine("1) 第一行：你是一位专业的网文小说创作者。 （不要使用提示词工程师口吻）");
        sb.AppendLine("2) 当前创作目标段：引用 {大纲名称}{章节标题}{场景标题}（部分可能为空，运行时自动替换）");
        sb.AppendLine("3) 服务说明段：服务于创作模块（战略大纲/分卷设计/章节规划/章节蓝图），根据系统上下文中提供的设计数据和当前模块的字段结构，生成可执行的创作方案");
        sb.AppendLine("4) 创作要求段：参考系统上下文中的设计规则、大纲规划和已有创作内容；与上下文中的设计数据保持一致，不引入未设定的新元素；叙事节奏张弛有度，情节推进合理有悬念");
        sb.AppendLine("5) 最后一行：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</content_structure>");
        sb.AppendLine();
        sb.AppendLine("<layout_rules mandatory=\"true\">");
        sb.AppendLine("每个段落之间必须空一行，避免所有内容挤在一起。");
        sb.AppendLine("</layout_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildUnifiedGenericMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        if (string.Equals(context.PromptSubCategory, "业务提示词", System.StringComparison.Ordinal))
        {
            sb.AppendLine("<role>Expert prompt engineer for novel writing software. Task: write a System Prompt for the business generation template (write to JSON 'content' field), usable directly in chapter generation.</role>");
            sb.AppendLine("该提示词由 SeedBusinessPromptIfEmpty 落盘为模板文件，运行时由 AutoRewriteEngine 注入到章节生成流程。");
            sb.AppendLine("请严格复刻内置 GenerationBusinessPrompt 的结构风格。");
            sb.AppendLine();
            sb.AppendLine("<placeholder_rules mandatory=\"true\">");
            sb.AppendLine("业务提示词不使用任何占位符（包括 {上下文数据}）。");
            sb.AppendLine("创作规格约束会在运行时作为「上方」上下文注入，请在文案中使用「上方创作规格约束」「创作规格」来引用。");
            sb.AppendLine("</placeholder_rules>");
            sb.AppendLine();
            sb.AppendLine("<content_style mandatory=\"true\">");
            sb.AppendLine("内置模板使用 XML 壳结构排版，生成的 content 必须复刻：");
            sb.AppendLine("- 顶层用 <role> 定义角色，<spec_priority immutable=\"true\"> 说明规格优先，<writing_techniques> 写作技法，<hard_constraints> 硬性约束");
            sb.AppendLine("- 段落之间必须空一行");
            sb.AppendLine("- 不要使用 Markdown #### 标题或【】中文方括号");
            sb.AppendLine("</content_style>");
            sb.AppendLine();
            sb.AppendLine("<content_structure mandatory=\"true\">");
            sb.AppendLine("1) <role>：定义 AI 扮演的角色（如「小说初稿生成器」），核心任务是严格遵循上方创作规格约束生成章节初稿");
            sb.AppendLine("2) <spec_priority immutable=\"true\">：3 条要点，明确写作风格/叙事视角/情感基调/题材边界一律以上方创作规格约束为准；必须包含/必须避免的元素必须严格执行；本规则仅补充创作规格未覆盖的写作技法层面");
            sb.AppendLine("3) <writing_techniques>：4 条编号规则，每条含 2-3 个子弹点，覆盖「语言简洁不堆砌修辞」「句式清晰结构紧凑」「情绪表达服从题材基调」「环境描写服从题材氛围」");
            sb.AppendLine("4) <hard_constraints>：6 条编号约束，必须覆盖「规格遵从」「设定保护」「剧情一致」「叙事视角」「字数控制」「纯正文输出（直接输出小说正文，禁止输出AI过渡语）」");
            sb.AppendLine("</content_structure>");
            sb.AppendLine();
            sb.AppendLine("<output_protocol mandatory=\"true\">");
            sb.AppendLine("业务提示词运行时输出为纯正文小说内容，不是 JSON。");
            sb.AppendLine("content 中不得要求目标 AI 输出 JSON 或结构化格式。");
            sb.AppendLine("</output_protocol>");
            sb.AppendLine();
            sb.AppendLine("<quality_requirements>");
            sb.AppendLine("- 写作技法规则必须具体可操作（如「允许使用常见动词和副词」），不要空泛的「写得更好」");
            sb.AppendLine("- 硬性约束必须明确无歧义，每条用加粗关键词开头");
            sb.AppendLine("- 所有内容必须围绕「章节初稿生成」场景，不要混入对话/问答场景");
            sb.AppendLine("</quality_requirements>");
            sb.AppendLine();
            sb.AppendLine("<output_format type=\"json\">");
            sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
            sb.AppendLine("{");
            sb.AppendLine("  \"content\": \"完整的业务 System Prompt（必填，换行用\\\\n）\",");
            sb.AppendLine("  \"name\": \"模板名称（如：小说初稿生成器）\",");
            sb.AppendLine("  \"description\": \"一句话描述用途\",");
            sb.AppendLine("  \"tags\": [\"章节生成\", \"业务提示词\", \"初稿\"]");
            sb.AppendLine("}");
            sb.AppendLine("</output_format>");
            return sb.ToString();
        }
        sb.AppendLine("<role>Expert prompt engineer. Task: generate a System Prompt for unified template use in business runtime.</role>");
        sb.AppendLine();
        sb.AppendLine("<strict_requirements>");
        sb.AppendLine("1. 只使用系统提供的输入变量占位符。不得使用 {上下文数据}（上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("2. 不要在模板中包含任何输出格式/字段清单/解析协议；最终输出协议由系统运行时字段契约统一追加。");
        sb.AppendLine("3. 不要在模板中包含示例占位符（如[要点1]、（100-200字）等）。");
        sb.AppendLine("4. 最后一行必须包含：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</strict_requirements>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildValidateMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<role>Expert prompt engineer specializing in novel writing software validation modules. Task: generate a System Prompt for the validation module that can be used directly in business runtime.</role>");
        sb.AppendLine();
        sb.AppendLine("<module_description>");
        sb.AppendLine("校验模板用于章节一致性校验，运行时系统会追加具体的 JSON 输出协议和规则清单。");
        sb.AppendLine("你只需要生成通用的校验 SystemPrompt（角色定义+核心职责+校验原则+输出要求），不需要写具体的 JSON 协议细节。");
        sb.AppendLine("</module_description>");
        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("参考内置模板，content 必须包含：");
        sb.AppendLine("1) 第一行：你是一位专业的小说一致性校验专家。（不要使用提示词工程师口吻）");
        sb.AppendLine("2) 核心职责段：对照提供的设定数据，逐项检查章节正文是否与设定一致；发现矛盾时明确指出并给出修复建议；缺少设定数据时标记为「未校验」");
        sb.AppendLine("3) 校验原则段：以设定数据为唯一权威来源；区分「硬伤」（明确矛盾）和「软伤」（风格偏离）；问题描述要具体到原文位置");
        sb.AppendLine("4) 输出要求段：按系统指定的JSON格式输出校验结果，moduleResults必须覆盖全部规则清单，不得遗漏");
        sb.AppendLine("</content_structure>");
        sb.AppendLine();
        sb.AppendLine("<placeholder_rules>不需要任何占位符。上下文和规则清单由系统运行时单独注入。</placeholder_rules>");
        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"...\",");
        sb.AppendLine("  \"name\": \"...\",");
        sb.AppendLine("  \"description\": \"...\",");
        sb.AppendLine("  \"tags\": [\"...\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");
        return sb.ToString();
    }

    private static string BuildBusinessMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();

        var moduleLabel = context.ModuleType?.ToLower() switch
        {
            "design" => "design module",
            "generate" => "generate module",
            "validate" => "validate module",
            _ => "novel writing software"
        };
        sb.AppendLine($"<role>Expert prompt engineer specializing in novel writing software {moduleLabel}. Task: generate a high-quality System Prompt.</role>");

        sb.AppendLine();
        sb.AppendLine("<task>");
        sb.AppendLine($"为「{context.ModuleDisplayName}」功能编写 System Prompt。");
        if (!string.IsNullOrWhiteSpace(context.Description))
            sb.AppendLine($"功能说明：{context.Description}");
        if (!string.IsNullOrWhiteSpace(context.ExtraRequirement))
            sb.AppendLine($"额外需求：{context.ExtraRequirement}");
        sb.AppendLine("</task>");

        sb.AppendLine();
        sb.AppendLine("<input_variables>");
        sb.AppendLine("Prompt 中可以包含以下占位符，运行时会被替换：");
        if (context.InputVariableNames != null && context.InputVariableNames.Count > 0)
        {
            foreach (var v in context.InputVariableNames)
                sb.AppendLine($"- {{{v}}}：用户输入");
        }
        sb.AppendLine("注意：不得使用 {上下文数据} 占位符（上下文由系统运行时单独注入，不需要占位符）。");
        sb.AppendLine("</input_variables>");

        sb.AppendLine();
        sb.AppendLine("<field_alignment_rules mandatory=\"true\">");
        if (context.OutputFieldNames != null && context.OutputFieldNames.Count > 0)
        {
            sb.AppendLine("生成的业务 System Prompt（content）必须严格对齐本次提供的字段清单：");
            sb.AppendLine("1. System Prompt（content）中不得包含字段清单原文，也不得规定任何输出格式（例如Markdown标题/JSON示例）。");
            sb.AppendLine("2. 只允许使用本元提示词声明的输入变量占位符（如 {变量名}）。不得自造占位符。");
            sb.AppendLine("3. 最终输出协议与字段清单由系统在运行时统一追加；你只需要在 content 中描述角色与任务要求。");
        }
        else
        {
            sb.AppendLine("本次未提供固定输出字段清单（该分类为跨模块模板）。");
            sb.AppendLine("你生成的 System Prompt 不得写死任何固定字段标题，也不得自造字段列表。");
            sb.AppendLine("系统会在运行时根据具体页面的字段契约追加输出规范，你只需要：");
            sb.AppendLine("1. 正确定义角色定位与任务目标");
            sb.AppendLine("2. 使用并仅使用本元提示词声明的输入变量占位符（如 {变量名}）");
            sb.AppendLine("3. 明确要求最终输出必须严格遵守运行时追加的字段契约");
        }
        sb.AppendLine("</field_alignment_rules>");

        sb.AppendLine();
        sb.AppendLine("<content_structure mandatory=\"true\">");
        sb.AppendLine("生成的 content 必须分区清晰，至少包含以下段落（每段之间必须空一行分隔）：");
        sb.AppendLine("1) 第一行：用业务口吻定义角色定位（不要使用提示词工程师口吻）");
        sb.AppendLine("2) 当前任务段：引用输入变量占位符（如{变量名}）");
        sb.AppendLine("3) 核心任务段：描述要产出的业务内容要求（具体、可执行、与系统上下文一致）");
        sb.AppendLine("4) 最后一行：严格按照输出要求中指定的字段名输出 JSON 对象");
        sb.AppendLine("</content_structure>");

        sb.AppendLine();
        sb.AppendLine("<quality_requirements mandatory=\"true\">");
        sb.AppendLine("不得包含示例占位符（如[要点1]、（100-200字）等）。");
        sb.AppendLine("内容要具体、可执行、与系统上下文保持一致。");
        sb.AppendLine("</quality_requirements>");

        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"完整的 System Prompt（必填，换行用\\\\n）\",");
        sb.AppendLine("  \"name\": \"模板名称\",");
        sb.AppendLine("  \"description\": \"一句话描述\",");
        sb.AppendLine("  \"tags\": [\"标签1\", \"标签2\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");

        return sb.ToString();
    }

    private static string BuildSpecMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<role>Expert prompt engineer specializing in novel writing software Spec (creative spec) module. Task: generate a high-quality Spec System Prompt.</role>");
        sb.AppendLine();
        sb.AppendLine("<task>");
        sb.AppendLine($"为「{context.ModuleDisplayName}」类型的小说创作规格编写 System Prompt。");
        if (!string.IsNullOrWhiteSpace(context.Description))
            sb.AppendLine($"功能说明：{context.Description}");
        if (!string.IsNullOrWhiteSpace(context.ExtraRequirement))
            sb.AppendLine($"额外需求：{context.ExtraRequirement}");
        sb.AppendLine("</task>");

        sb.AppendLine();
        sb.AppendLine("<spec_core_purpose>");
        sb.AppendLine("Spec 模板是整个创作流水线的「0层强约束」，所有下游模块（素材设计、小说设计、章节生成、校验）都会以 Spec 内容作为最高优先级约束。");
        sb.AppendLine("因此 Spec 模板不仅要包含可解析的结构化字段，还必须包含深度的题材约束内容，这些内容会作为原文注入到下游 AI 的系统提示中。");
        sb.AppendLine("</spec_core_purpose>");

        sb.AppendLine();
        sb.AppendLine("<spec_format_rules>");
        sb.AppendLine("Spec 模板使用「【字段名】值」格式，系统通过正则解析。content 必须包含以下全部内容：");
        sb.AppendLine("注意：本元提示词中的字数范围仅用于生成指导，最终生成的 Spec Prompt（content）中不得出现任何字数提示、方括号占位符（如[类型]）。");
        sb.AppendLine();
        sb.AppendLine("一、题材深度约束（非结构化，但极其重要，直接作为强约束文本注入下游）：");
        sb.AppendLine("【题材锚点】详细描述该题材的核心元素和标志性特征（100-200字），例如玄幻的「多级境界体系、宗门势力博弈、秘境奇遇」等");
        sb.AppendLine("【世界观规则】该题材下的核心世界观规则和逻辑约束（100-200字），例如「力量提升必须有代价与条件；战斗遵循境界差距与克制关系」等");
        sb.AppendLine("【节奏要求】该题材下的章节节奏模式（50-100字），例如「开局冲突/危机→快速升级/奇遇→强者对决与反转→结尾留悬念」");
        sb.AppendLine();
        sb.AppendLine("二、结构化字段（系统通过正则解析，字段名必须逐字一致）：");
        sb.AppendLine("【写作风格】描述整体写作风格（如：热血激昂、轻松幽默、沉稳大气）");
        sb.AppendLine("【叙述视角】叙事视角类型（如：第三人称限知、第一人称、全知视角）");
        sb.AppendLine("【情感基调】情感基调定位（如：紧张悬疑、温馨治愈、冷峻客观）");
        sb.AppendLine("【目标字数】每章目标字数（纯数字，如：3500）");
        sb.AppendLine("【段落长度】平均段落长度（纯数字，如：200）");
        sb.AppendLine("【对话比例】对话占比百分比（纯数字，如：30）");
        sb.AppendLine("【必须包含】必须包含的元素（逗号分隔，如：境界突破,强者对决）");
        sb.AppendLine("【必须避免】必须避免的内容（逗号分隔，如：现代科技,过度说教）");
        sb.AppendLine("</spec_format_rules>");

        sb.AppendLine();
        sb.AppendLine("<content_template note=\"IMPORTANT: field names like 【题材锚点】 are parsed by regex — DO NOT change them in the generated content.\">");
        sb.AppendLine("生成的 content 必须严格遵循以下结构（参考内置 Spec-玄幻 模板）：");
        sb.AppendLine("```");
        sb.AppendLine($"你是一位专业的{context.ModuleDisplayName}小说写作助手。");
        sb.AppendLine();
        sb.AppendLine("【题材锚点】[详细描述该题材的核心元素、标志性特征、子类型关键词]");
        sb.AppendLine("【世界观规则】[该题材的核心逻辑约束、力量体系规则、冲突驱动模式]");
        sb.AppendLine("【写作风格】[风格描述]");
        sb.AppendLine("【叙述视角】[视角类型]");
        sb.AppendLine("【情感基调】[基调描述]");
        sb.AppendLine("【目标字数】[数字]");
        sb.AppendLine("【段落长度】[数字]");
        sb.AppendLine("【对话比例】[数字]");
        sb.AppendLine("【节奏要求】[章节节奏模式描述]");
        sb.AppendLine("【必须包含】[逗号分隔的元素列表]");
        sb.AppendLine("【必须避免】[逗号分隔的禁忌列表]");
        sb.AppendLine("```");
        sb.AppendLine("</content_template>");

        sb.AppendLine();
        sb.AppendLine("<quality_requirements>");
        sb.AppendLine("1. 题材锚点和世界观规则必须有深度，不能是空泛描述，要体现该题材区别于其他题材的独特约束");
        sb.AppendLine("2. 必须包含和必须避免要具体、可操作，避免过于笼统的描述");
        sb.AppendLine("3. 所有字段内容必须相互一致，风格/基调/节奏/元素要匹配同一题材");
        sb.AppendLine("</quality_requirements>");

        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"完整的 Spec Prompt（必填，换行用\\\\n）\",");
        sb.AppendLine($"  \"name\": \"{context.ModuleDisplayName}Spec\",");
        sb.AppendLine($"  \"description\": \"适用于{context.ModuleDisplayName}类小说的创作规格\",");
        sb.AppendLine("  \"tags\": [\"类型标签\", \"Spec\", \"创作规格\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");

        return sb.ToString();
    }

    private static string BuildAIGCMetaPrompt(PromptGenerationContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<role>Expert prompt engineer specializing in novel writing software AIGC inline editing modules. Task: generate a complete System Prompt for the AIGC module.</role>");
        sb.AppendLine();
        sb.AppendLine("<task>");
        sb.AppendLine($"为「{context.ModuleDisplayName}」功能编写完整的 System Prompt。");
        if (!string.IsNullOrWhiteSpace(context.Description))
            sb.AppendLine($"功能说明：{context.Description}");
        if (!string.IsNullOrWhiteSpace(context.ExtraRequirement))
            sb.AppendLine($"额外需求：{context.ExtraRequirement}");
        sb.AppendLine("</task>");

        sb.AppendLine();
        sb.AppendLine("<module_spec>");
        sb.AppendLine("AIGC 模板用于内联编辑场景（润色/精简/情感强化/场景渲染/对话打磨/节奏调整等）。");
        sb.AppendLine("生成的 content 必须是一份完整的多段 System Prompt，包含：");
        sb.AppendLine("1. 角色定义：用业务口吻定义 AI 的专业身份（如「资深小说编辑」），不要使用提示词工程师口吻");
        sb.AppendLine("2. 任务段：明确说明要对用户提供的文字做什么操作");
        sb.AppendLine("3. 核心原则段：列出 3-5 条具体的操作原则（保持什么不变、重点优化什么、避免什么）");
        sb.AppendLine("4. 最后一行：直接输出修改后的文本，不要附加任何解释。");
        sb.AppendLine("</module_spec>");
        sb.AppendLine();
        sb.AppendLine("<strict_constraints>");
        sb.AppendLine("- 不需要结构化输出格式（不需要 JSON），AI 运行时直接返回修改后的文本");
        sb.AppendLine("- 不需要占位符，用户选中的文本会作为用户消息直接发送");
        sb.AppendLine("- 原则要具体可操作，不要空泛的「写得更好」类描述");
        sb.AppendLine("</strict_constraints>");

        sb.AppendLine();
        sb.AppendLine("<reference_template name=\"AIGC润色\" note=\"IMPORTANT: structure reference only. DO NOT copy this text into generated content.\">");
        sb.AppendLine("```");
        sb.AppendLine("你是一位深谙中文文学表达的资深小说编辑，擅长在保持原作灵魂的前提下，让文字焕发更强的文学感染力。");
        sb.AppendLine();
        sb.AppendLine("任务：润色用户提供的小说文字，使语句更加流畅优美、富有文学性。");
        sb.AppendLine();
        sb.AppendLine("核心原则：");
        sb.AppendLine("- 保持原文的叙事结构、人物关系和情节走向不变，不增删情节内容");
        sb.AppendLine("- 重点优化遣词造句的精准度、句式的多样性、修辞手法的自然运用");
        sb.AppendLine("- 避免过度华丽导致风格偏移，润色程度应与原文气质匹配");
        sb.AppendLine();
        sb.AppendLine("直接输出润色后的文本，不要附加任何解释。");
        sb.AppendLine("```");
        sb.AppendLine("</reference_template>");

        sb.AppendLine();
        sb.AppendLine("<output_format type=\"json\">");
        sb.AppendLine("只输出纯 JSON（不要用```代码块包裹）。");
        sb.AppendLine("{");
        sb.AppendLine("  \"content\": \"完整的 System Prompt（必填，换行用\\\\n）\",");
        sb.AppendLine("  \"name\": \"功能名称（如：情感深化、场景渲染）\",");
        sb.AppendLine("  \"description\": \"一句话描述用途\",");
        sb.AppendLine("  \"tags\": [\"AIGC\", \"润色\", \"具体标签\"]");
        sb.AppendLine("}");
        sb.AppendLine("</output_format>");

        return sb.ToString();
    }
}
