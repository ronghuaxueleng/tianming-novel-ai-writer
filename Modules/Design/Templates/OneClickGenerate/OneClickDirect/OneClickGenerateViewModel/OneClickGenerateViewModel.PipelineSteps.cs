using System;
using System.ComponentModel;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region 管线步骤定义

        private static readonly PipelineStepDefinition[] StepDefinitions = new[]
        {
            new PipelineStepDefinition(1, "创作模板", IconHelper.TryGet("Icon.Sparkle"), typeof(TM.Modules.Design.Templates.CreativeMaterials.CreativeMaterialsView), true,
                new ExtraFieldDef[] {
                    new("SourceBookName", "来源拆书", false, IsDropdown: true),
                    new("Genre", "题材类型", true, IsDropdown: true),
                    new("GoldenChapter", "黄金三章", false, IsDropdown: true),
                }, TitleColorGroup: "Template"),
            new PipelineStepDefinition(2, "世界观规则", IconHelper.TryGet("Icon.Globe"), typeof(TM.Modules.Design.GlobalSettings.WorldRules.WorldRulesView), false, Array.Empty<ExtraFieldDef>(), TitleColorGroup: "World"),
            new PipelineStepDefinition(3, "角色规则", IconHelper.TryGet("Icon.User"), typeof(TM.Modules.Design.Elements.CharacterRules.CharacterRulesView), false, Array.Empty<ExtraFieldDef>(), TitleColorGroup: "DesignElements"),
            new PipelineStepDefinition(4, "势力规则", IconHelper.TryGet("Icon.Package"), typeof(TM.Modules.Design.Elements.FactionRules.FactionRulesView), false, Array.Empty<ExtraFieldDef>(), TitleColorGroup: "DesignElements"),
            new PipelineStepDefinition(5, "位置规则", IconHelper.TryGet("Icon.Target"), typeof(TM.Modules.Design.Elements.LocationRules.LocationRulesView), false, Array.Empty<ExtraFieldDef>(), TitleColorGroup: "DesignElements"),
            new PipelineStepDefinition(6, "剧情规则", IconHelper.TryGet("Icon.Document"), typeof(TM.Modules.Design.Elements.PlotRules.PlotRulesView), false,
                new ExtraFieldDef[] {
                    new("TargetVolume", "总卷数", true),
                }, TitleColorGroup: "DesignElements"),
            new PipelineStepDefinition(7, "大纲设计", IconHelper.TryGet("Icon.Document"), typeof(TM.Modules.Generate.GlobalSettings.Outline.OutlineView), false,
                new ExtraFieldDef[] {
                    new("TotalChapterCount", "总章节数", true),
                }, TitleColorGroup: "Outline"),
            new PipelineStepDefinition(8, "分卷设计", IconHelper.TryGet("Icon.Books"), typeof(TM.Modules.Generate.Elements.VolumeDesign.VolumeDesignView), false, Array.Empty<ExtraFieldDef>(), HideCount: true, TitleColorGroup: "GenerateElements", HideCategory: true, CategoryHint: "将依据大纲自动规划分卷结构"),
            new PipelineStepDefinition(9, "章节设计", IconHelper.TryGet("Icon.Clipboard"), typeof(TM.Modules.Generate.Elements.Chapter.ChapterView), false, Array.Empty<ExtraFieldDef>(), HideCount: true, TitleColorGroup: "GenerateElements", AutoExpandCategories: true, CategoryHint: "订阅分卷设计，全量生成"),
            new PipelineStepDefinition(10, "蓝图设计", IconHelper.TryGet("Icon.Palette"), typeof(TM.Modules.Generate.Elements.Blueprint.BlueprintView), false, Array.Empty<ExtraFieldDef>(), HideCount: true, TitleColorGroup: "GenerateElements", AutoExpandCategories: true, CategoryHint: "订阅分卷/章节，全量生成", RequiredPreviousStepIndex: 9),
        };

        #endregion
    }
}
