using System.Text;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Developer;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Behavior;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Dialog;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Business;
using TM.Services.Framework.AI.SemanticKernel.Prompts.Spec;
using TM.Services.Framework.AI.Interfaces.Prompts;
using System;
using System.Linq;

namespace TM.Services.Framework.AI.SemanticKernel.Prompts
{
    public static class PromptLibrary
    {
        #region L1

        public static string GetDeveloperPrompt() => DeveloperPromptProvider.BaseDeveloperMessage;

        #endregion

        #region L2

        public static string GetModeTemplate(ChatMode mode) => BehaviorPromptProvider.GetModeTemplate(mode);

        public static bool IsIdentityQuestion(string input) => BehaviorPromptProvider.IsIdentityQuestion(input);

        #endregion

        #region L3

        public static string GetAnalysisAnswerSpec() => DialogPromptProvider.AnalysisAnswerSpec;

        #endregion

        #region L4

        public static string GetDialogueBusinessPrompt() => BusinessPromptProvider.DialogueBusinessPrompt;

        #endregion

        #region L5

        public static string BuildSpecPrompt(CreativeSpec? spec) => SpecPromptProvider.BuildSpecPrompt(spec);

        #endregion

        #region Build

        public static ChatPromptParts BuildPromptParts(
            ChatMode mode,
            string userInput,
            bool includeBusinessPrompt = false,
            CreativeSpec? spec = null)
        {
            var systemPrompt = BuildSystemPromptForMode(mode, includeBusinessPrompt, spec);
            var userPrompt = BehaviorPromptProvider.BuildUserPrompt(mode, userInput);

            return new ChatPromptParts
            {
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt
            };
        }

        private static string BuildSystemPromptForMode(
            ChatMode mode,
            bool includeBusinessPrompt,
            CreativeSpec? spec)
        {
            var sb = new StringBuilder();

            sb.Append(GetDeveloperPrompt());

            sb.Append("\n\n");
            sb.Append(GetModeTemplate(mode));

            if (includeBusinessPrompt && spec != null)
            {
                var rawPrompt = LoadSpecTemplateRawPrompt(spec.TemplateName);
                if (!string.IsNullOrWhiteSpace(rawPrompt))
                {
                    sb.Append("\n\n");
                    sb.Append("<genre_spec priority=\"highest\" source=\"prompt_library\">\n");
                    sb.Append(rawPrompt);
                    sb.Append("\n</genre_spec>");
                }
            }

            if (includeBusinessPrompt)
            {
                sb.Append("\n\n");
                sb.Append(GetDialogueBusinessPrompt());
            }

            if (spec != null)
            {
                var specPrompt = BuildSpecPrompt(spec);
                if (!string.IsNullOrWhiteSpace(specPrompt))
                {
                    sb.Append("\n\n");
                    sb.Append(specPrompt);
                }
            }

            sb.Append("\n\n");
            sb.Append(GetAnalysisAnswerSpec());

            return sb.ToString();
        }

        private static string? LoadSpecTemplateRawPrompt(string? templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return null;
            try
            {
                var repo = ServiceLocator.Get<IPromptRepository>();
                var specTemplate = repo.GetAllTemplates()
                    .FirstOrDefault(t => t.Name == templateName
                        && t.Tags != null && t.Tags.Contains("Spec"));
                return specTemplate?.SystemPrompt;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PromptLibrary] 加载Spec模板失败: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Shortcuts

        public static ChatPromptParts BuildSimplePromptParts(ChatMode mode, string userInput)
        {
            return BuildPromptParts(mode, userInput, includeBusinessPrompt: false, spec: null);
        }

        public static ChatPromptParts BuildWritingPromptParts(ChatMode mode, string userInput, CreativeSpec? spec = null)
        {
            return BuildPromptParts(mode, userInput, includeBusinessPrompt: true, spec: spec);
        }

        #endregion
    }
}
