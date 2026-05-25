using System.Text.Json.Serialization;

namespace TM.Framework.UI.Workspace.Services.Spec
{
    public class CreativeSpec
    {
        #region 元信息

        [JsonPropertyName("templateName")]
        public string? TemplateName { get; set; }

        #endregion

        #region 写作风格

        [JsonPropertyName("writingStyle")]
        public string? WritingStyle { get; set; }

        [JsonPropertyName("pov")]
        public string? Pov { get; set; }

        [JsonPropertyName("tone")]
        public string? Tone { get; set; }

        #endregion

        #region 格式约束

        [JsonPropertyName("targetWordCount")]
        public int? TargetWordCount { get; set; }

        [JsonPropertyName("paragraphLength")]
        public int? ParagraphLength { get; set; }

        [JsonPropertyName("dialogueRatio")]
        public double? DialogueRatio { get; set; }

        #endregion

        #region 内容约束

        [JsonPropertyName("mustInclude")]
        public string[]? MustInclude { get; set; }

        [JsonPropertyName("mustAvoid")]
        public string[]? MustAvoid { get; set; }

        [JsonPropertyName("characterFocus")]
        public string[]? CharacterFocus { get; set; }

        #endregion

        #region 生成设置

        [JsonPropertyName("polishMode")]
        public int? PolishMode { get; set; }

        [JsonPropertyName("polishModel")]
        public int? PolishModel { get; set; }

        [JsonPropertyName("wordCountControl")]
        public int? WordCountControl { get; set; }

        [JsonPropertyName("polishControl")]
        public int? PolishControl { get; set; }

        public const int DefaultPolishMode = 1;
        public const int DefaultPolishModel = 0;
        public const int DefaultWordCountControl = 0;
        public const int DefaultPolishControl = 1;

        public static int GetEffectivePolishMode(CreativeSpec? spec)
        {
            var mode = spec?.PolishMode ?? DefaultPolishMode;
            if (mode < 0) return 0;
            if (mode > 2) return 2;
            return mode;
        }

        public static int GetEffectivePolishModel(CreativeSpec? spec)
        {
            var model = spec?.PolishModel ?? DefaultPolishModel;
            return model == 1 ? 1 : DefaultPolishModel;
        }

        public static int GetEffectiveWordCountControl(CreativeSpec? spec)
        {
            var v = spec?.WordCountControl ?? DefaultWordCountControl;
            if (v < 0) return 0;
            if (v > 2) return 2;
            return v;
        }

        public static int GetEffectivePolishControl(CreativeSpec? spec)
        {
            var v = spec?.PolishControl ?? DefaultPolishControl;
            if (v < 0) return 0;
            if (v > 2) return 2;
            return v;
        }

        #endregion

        #region 章节特定

        [JsonPropertyName("sceneDescription")]
        public string? SceneDescription { get; set; }

        [JsonPropertyName("emotionalArc")]
        public string? EmotionalArc { get; set; }

        [JsonPropertyName("plotPoints")]
        public string? PlotPoints { get; set; }

        #endregion

        public static CreativeSpec Merge(CreativeSpec? baseSpec, CreativeSpec? overrideSpec)
        {
            if (baseSpec == null && overrideSpec == null)
                return new CreativeSpec();

            if (baseSpec == null)
                return overrideSpec!;

            if (overrideSpec == null)
                return baseSpec;

            return new CreativeSpec
            {
                TemplateName = overrideSpec.TemplateName ?? baseSpec.TemplateName,

                WritingStyle = overrideSpec.WritingStyle ?? baseSpec.WritingStyle,
                Pov = overrideSpec.Pov ?? baseSpec.Pov,
                Tone = overrideSpec.Tone ?? baseSpec.Tone,

                TargetWordCount = overrideSpec.TargetWordCount ?? baseSpec.TargetWordCount,
                ParagraphLength = overrideSpec.ParagraphLength ?? baseSpec.ParagraphLength,
                DialogueRatio = overrideSpec.DialogueRatio ?? baseSpec.DialogueRatio,

                MustInclude = MergeArrays(baseSpec.MustInclude, overrideSpec.MustInclude),
                MustAvoid = MergeArrays(baseSpec.MustAvoid, overrideSpec.MustAvoid),
                CharacterFocus = overrideSpec.CharacterFocus ?? baseSpec.CharacterFocus,

                PolishMode = overrideSpec.PolishMode ?? baseSpec.PolishMode,
                PolishModel = overrideSpec.PolishModel ?? baseSpec.PolishModel,
                WordCountControl = overrideSpec.WordCountControl ?? baseSpec.WordCountControl,
                PolishControl = overrideSpec.PolishControl ?? baseSpec.PolishControl,

                SceneDescription = overrideSpec.SceneDescription ?? baseSpec.SceneDescription,
                EmotionalArc = overrideSpec.EmotionalArc ?? baseSpec.EmotionalArc,
                PlotPoints = overrideSpec.PlotPoints ?? baseSpec.PlotPoints
            };
        }

        private static string[]? MergeArrays(string[]? arr1, string[]? arr2)
        {
            if (arr1 == null && arr2 == null)
                return null;

            if (arr1 == null)
                return arr2;

            if (arr2 == null)
                return arr1;

            var set = new System.Collections.Generic.HashSet<string>(arr1);
            foreach (var item in arr2)
            {
                set.Add(item);
            }
            return set.Count > 0 ? System.Linq.Enumerable.ToArray(set) : null;
        }

        public string BuildPromptFragment()
            => TM.Services.Framework.AI.SemanticKernel.Prompts.PromptLibrary.BuildSpecPrompt(this);
    }
}
