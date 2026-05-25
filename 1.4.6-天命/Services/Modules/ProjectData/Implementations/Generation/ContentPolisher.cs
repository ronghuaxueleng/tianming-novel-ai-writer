using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public class ContentPolisher
    {
        private const string StyleMimicryPrompt = @"<role>Style Mimicry Expert. Core task: rewrite Chinese novel text to closely match human writing style, making it extremely difficult for AI detection tools to identify.</role>

<polish_rules>

### 1. 增加冗余与解释性
将简洁的动词或动词短语替换为更长的、带有动作过程描述的短语。
-   ""管理"" → ""开展...的管理工作"" 或 ""进行管理""
-   ""交互"" → ""进行交互"" 或 ""开展交互""
-   ""配置"" → ""进行配置""
-   ""处理"" → ""去处理...工作""
-   ""恢复"" → ""进行恢复""
-   ""实现"" → ""得以实现"" 或 ""来实现""
-   ""分析"" → ""对…进行分析""
-   ""验证"" → ""开展相关的验证工作""

在句子中添加语法上允许但非必需的词语，使句子更饱满。
-   适当增加 ""了""、""的""、""地""、""所""、""会""、""可以""、""这个""、""方面""、""当中"" 等。
-   ""提供功能"" → ""有...功能"" 或 ""拥有...的功能""

### 2. 系统性词汇替换
-   不要出现生僻词或生僻字，将其换成常用语
-   ""囊括"" → ""包括""
-   ""采用 / 使用 "" → ""运用 / 选用"" / ""把...当作...来使用""
-   ""基于"" → ""鉴于"" / ""基于...来开展"" / ""凭借""
-   ""利用"" → ""借助"" / ""运用"" / ""凭借""
-   ""通过"" → ""借助"" / ""依靠"" / ""凭借""
-   ""和 / 及 / 与"" → ""以及"" (尤其在列举多项时)
-   ""并"" → ""并且"" / ""还"" / ""同时""
-   ""其"" → ""它"" / ""其"" (可根据语境选择，用""它""更自然)
-   ""关于"" → ""有关于""
-   ""为了"" → ""为了能够""
-   ""特点"" → ""特性""
-   ""原因"" → ""缘由"" / ""其主要原因包括...""
-   ""符合"" → ""契合""
-   ""适合"" → ""适宜""
-   ""提升 / 提高"" → ""对…进行提高"" / ""得到进一步的提升""
-   ""极大(地)"" → ""极大程度(上)""
-   ""立即"" → ""马上""

### 3. 括号内容处理
对于原文中用于解释、举例或说明缩写的括号 `(...)` 或 `（...）`：
-   **优先整合:** 尝试将括号内的信息自然地融入句子，使用 ""也就是""、""即""、""比如""、""像"" 等引导词。
    -   示例：`ORM（对象关系映射）` → `对象关系映射即ORM` 或 `ORM也就是对象关系映射`
    -   示例：`功能（如ORM、Admin）` → `功能，比如ORM、Admin` 或 `功能，像ORM、Admin等`
-   **谨慎省略:** 如果整合后语句极其冗长或别扭，并且括号内容并非核心关键信息，可以考虑省略。

-   示例：`视图 (views.py) 中` → `视图文件views.py中`
-   示例：`权限类 (admin_panel.permissions)` → `权限类 admin_panel.permissions`

### 4. 句式微调与自然化
-   **使用""把""字句:** 在合适的场景下，倾向于使用""把""字句。
    -   示例：""会将对象移动"" → ""会把这个对象移动""
-   **条件句式转换:** 将较书面的条件句式改为稍口语化的形式。
    -   示例：""若…，则…"" → ""要是...，那就..."" 或 ""如果...，就...""
-   **结构切换:** 进行名词化与动词化结构的相互转换。
    -   示例：""为了将…解耦"" → ""为了实现...的解耦""
-   **增加连接词:** 在句首或句中适时添加""那么""、""这样一来""、""同时""等词。
---
以上只是基本举例，如果文章中有和以上例子相似的，也要根据例子灵活修改

</polish_rules>

<strict_rules>
1. 核心内容不变且专有名词锁定: 修改后的文本必须保持原文完全相同的情节走向、人物关系、因果逻辑和世界观设定；原文中出现的所有人物姓名、势力名称、组织名称、地点名称必须原样保留，不得用代词（他/她/它/他们）或泛称替换，出现次数不少于原文。
2. 人称保持: 保持原文的叙事人称不变（原文是第一人称就保持第一人称，第三人称同理）。
3. 字数控制: 确保修改后的总字数与原文基本一致，避免不必要的冗长。
4. 结构保持: 维持原文的段落划分不变。不要遗漏原文中的任何段落或情节。
5. 纯正文输出: 你的唯一输出是修改后的完整正文。不要附加任何解释、注释或标签。
6. 输出语言一致性: 只处理中文文本，输出中文。
7. NEVER output ---CHANGES--- separator or any JSON change summary. Output ONLY the polished prose.
</strict_rules>";

        private const string ChangesSeparator = "---CHANGES---";

        private static Regex ChangesSeparatorLineRegex => GenerationGate.ChangesSeparatorLineRegex;
        private static Regex MdChangesHeaderRegex => GenerationGate.MdChangesHeaderRegex;

        public async Task<PolishResult> PolishAsync(string rawContent, CancellationToken ct = default)
        {
            var result = new PolishResult { OriginalContent = rawContent };

            try
            {
                var separatorIndex = rawContent.IndexOf(ChangesSeparator, StringComparison.Ordinal);
                if (separatorIndex < 0)
                {
                    var regexMatch = ChangesSeparatorLineRegex.Match(rawContent);
                    if (regexMatch.Success)
                        separatorIndex = regexMatch.Index;
                }
                if (separatorIndex < 0)
                {
                    var mdMatch = MdChangesHeaderRegex.Match(rawContent);
                    if (mdMatch.Success)
                        separatorIndex = mdMatch.Index;
                }
                string contentPart;
                string? changesPart;
                if (separatorIndex < 0)
                {
                    TM.App.Log("[ContentPolisher] 未找到CHANGES分隔符，将全文作为正文润色");
                    contentPart = rawContent.TrimEnd();
                    changesPart = null;
                }
                else
                {
                    contentPart = rawContent.Substring(0, separatorIndex).TrimEnd();
                    changesPart = rawContent.Substring(separatorIndex);
                }

                var polishPrompt = $"{StyleMimicryPrompt}\n\n<source_text>\n{contentPart}\n</source_text>";
                const int maxRetries = 2;
                var aiSvc = ServiceLocator.Get<AIService>();
                var aiResult = await aiSvc.GenerateAsync(polishPrompt, ct);
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.Content))
                        break;
                    var delayMs = 1000 + new Random().Next(0, 2001);
                    TM.App.Log($"[ContentPolisher] 润色第{attempt + 1}次重试（等待{delayMs}ms）...");
                    await Task.Delay(delayMs, ct);
                    aiResult = await aiSvc.GenerateAsync(polishPrompt, ct);
                }

                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    result.Success = false;
                    result.ErrorMessage = $"润色失败: {aiResult.ErrorMessage ?? "AI未返回内容"}";
                    result.PolishedContent = rawContent;
                    TM.App.Log($"[ContentPolisher] 润色失败（已重试{maxRetries}次），使用原文: {result.ErrorMessage}");
                    return result;
                }

                var polishedContent = aiResult.Content.Trim();

                var cleanIdx = polishedContent.IndexOf(ChangesSeparator, StringComparison.Ordinal);
                if (cleanIdx < 0)
                {
                    var cleanMatch = ChangesSeparatorLineRegex.Match(polishedContent);
                    if (cleanMatch.Success) cleanIdx = cleanMatch.Index;
                }
                if (cleanIdx > 0)
                {
                    polishedContent = polishedContent.Substring(0, cleanIdx).TrimEnd();
                    TM.App.Log("[ContentPolisher] 清洗润色结果中残留的CHANGES段");
                }

                result.PolishedContent = changesPart != null
                    ? $"{polishedContent}\n\n{changesPart}"
                    : polishedContent;
                result.Success = true;
                result.ContentWithoutChanges = polishedContent;

                TM.App.Log($"[ContentPolisher] 润色成功，原文{contentPart.Length}字 → 润色后{polishedContent.Length}字{(changesPart == null ? "（无CHANGES块）" : "")}");
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "润色已取消";
                result.PolishedContent = rawContent;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"润色异常: {ex.Message}";
                result.PolishedContent = rawContent;
                TM.App.Log($"[ContentPolisher] 润色异常: {ex.Message}");
                return result;
            }
        }
    }

    public class PolishResult
    {
        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public string OriginalContent { get; set; } = string.Empty;

        public string PolishedContent { get; set; } = string.Empty;

        public string? ContentWithoutChanges { get; set; }
    }
}
