using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.Common.Constants
{
    public static class NavigationDefinitions
    {
        #region 顶部Tab定义

        public static readonly TabDefinition[] WritingTabs = new[]
        {
            new TabDefinition(0, "Icon.Edit", "设计", "Design"),
            new TabDefinition(1, "Icon.Sparkle", "创作", "Generate"),
            new TabDefinition(2, "Icon.Search", "校验", "Validate"),
            new TabDefinition(3, "Icon.Robot", "智助", "SmartAssistant"),
        };

        public static readonly TabDefinition[] PersonalTabs = new[]
        {
            new TabDefinition(0, "Icon.User", "用户", "User"),
            new TabDefinition(1, "Icon.Palette", "界面", "Appearance"),
            new TabDefinition(2, "Icon.Bell", "通知", "Notifications"),
            new TabDefinition(3, "Icon.Settings", "系统", "SystemSettings"),
        };

        #endregion

        #region 左侧导航树定义 - 写作模块

        public static readonly ModuleNavigation SmartAssistant = new()
        {
            Name = "AI助手",
            Icon = "Icon.Robot",
            Type = "SmartAssistant",
            SubModules = new[]
            {
                new SubModuleNavigation("模型集成", "Icon.Chart", new[]
                {
                    new FunctionNavigation("模型管理", "Icon.Robot", typeof(TM.Modules.AIAssistant.ModelIntegration.ModelManagement.ModelManagementView), "TM/Modules/AIAssistant/ModelIntegration/ModelManagement/ModelManagementView"),
                    new FunctionNavigation("API统计", "Icon.TrendUp", typeof(TM.Modules.AIAssistant.ModelIntegration.UsageStatistics.UsageStatisticsView), "TM/Modules/AIAssistant/ModelIntegration/UsageStatistics/UsageStatisticsView"),
                    new FunctionNavigation("API告警", "Icon.Bell", typeof(TM.Modules.AIAssistant.ModelIntegration.Alert.AlertView), "TM/Modules/AIAssistant/ModelIntegration/Alert/AlertView"),
                }),
                new SubModuleNavigation("提示词工具", "Icon.Chat", new[]
                {
                    new FunctionNavigation("提示词管理", "Icon.Books", typeof(TM.Modules.AIAssistant.PromptTools.PromptManagement.PromptManagementView), "TM/Modules/AIAssistant/PromptTools/PromptManagement/PromptManagementView"),
                    new FunctionNavigation("版本测试", "Icon.Flask", typeof(TM.Modules.AIAssistant.PromptTools.VersionTesting.VersionTestingView), "TM/Modules/AIAssistant/PromptTools/VersionTesting/VersionTestingView"),
                }),
            }
        };

        public static readonly ModuleNavigation Validate = new()
        {
            Name = "校验",
            Icon = "Icon.Search",
            Type = "Validate",
            SubModules = new[]
            {
                new SubModuleNavigation("校验汇总", "Icon.Chart", new[]
                {
                    new FunctionNavigation("校验结果", "Icon.Clipboard", typeof(TM.Modules.Validate.ValidationSummary.ValidationResult.ValidationResultView), "TM/Modules/Validate/ValidationSummary/ValidationResult/ValidationResultView"),
                }),
                new SubModuleNavigation("校验介绍", "Icon.Book", new[]
                {
                    new FunctionNavigation("世界观校验", "Icon.Globe", typeof(TM.Modules.Validate.ValidationIntro.WorldviewIntro.WorldviewIntroView), "TM/Modules/Validate/ValidationIntro/WorldviewIntro/WorldviewIntroView"),
                    new FunctionNavigation("角色校验", "Icon.User", typeof(TM.Modules.Validate.ValidationIntro.CharacterIntro.CharacterIntroView), "TM/Modules/Validate/ValidationIntro/CharacterIntro/CharacterIntroView"),
                    new FunctionNavigation("剧情校验", "Icon.Book", typeof(TM.Modules.Validate.ValidationIntro.PlotIntro.PlotIntroView), "TM/Modules/Validate/ValidationIntro/PlotIntro/PlotIntroView"),
                    new FunctionNavigation("大纲校验", "Icon.Edit", typeof(TM.Modules.Validate.ValidationIntro.OutlineIntro.OutlineIntroView), "TM/Modules/Validate/ValidationIntro/OutlineIntro/OutlineIntroView"),
                    new FunctionNavigation("章节校验", "Icon.Chapter", typeof(TM.Modules.Validate.ValidationIntro.ChapterIntro.ChapterIntroView), "TM/Modules/Validate/ValidationIntro/ChapterIntro/ChapterIntroView"),
                    new FunctionNavigation("正文校验", "Icon.Document", typeof(TM.Modules.Validate.ValidationIntro.ContentIntro.ContentIntroView), "TM/Modules/Validate/ValidationIntro/ContentIntro/ContentIntroView"),
                }),
            }
        };

        public static readonly ModuleNavigation Design = new()
        {
            Name = "设计",
            Icon = "Icon.Edit",
            Type = "Design",
            SubModules = new[]
            {
                new SubModuleNavigation("智能拆书", "Icon.Robot", new[]
                {
                    new FunctionNavigation("拆书分析", "Icon.Robot", typeof(TM.Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView), "TM/Modules/Design/SmartParsing/BookAnalysis/BookAnalysisView"),
                    new FunctionNavigation("提炼分析", "Icon.Sparkle", typeof(TM.Modules.Design.SmartParsing.ContentRefinery.ContentRefineryView), "TM/Modules/Design/SmartParsing/ContentRefinery/ContentRefineryView"),
                }),
                new SubModuleNavigation("一键生成", "Icon.Lightning", new[]
                {
                    new FunctionNavigation("一键直出", "Icon.Lightning", typeof(TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect.OneClickGenerateView), "TM/Modules/Design/Templates/OneClickGenerate/OneClickDirect/OneClickGenerateView"),
                    new FunctionNavigation("短篇直出", "Icon.Book", typeof(TM.Modules.Design.Templates.OneClickGenerate.ShortStoryBlueprint.ShortStoryBlueprintView), "TM/Modules/Design/Templates/OneClickGenerate/ShortStoryBlueprint/ShortStoryBlueprintView"),
                }),
                new SubModuleNavigation("创作模板", "Icon.Lightbulb", new[]
                {
                    new FunctionNavigation("模板管理", "Icon.Lightbulb", typeof(TM.Modules.Design.Templates.CreativeMaterials.CreativeMaterialsView), "TM/Modules/Design/Templates/CreativeMaterials/CreativeMaterialsView"),
                }),
                new SubModuleNavigation("全局设定", "Icon.Globe", new[]
                {
                    new FunctionNavigation("世界观规则", "Icon.Globe", typeof(TM.Modules.Design.GlobalSettings.WorldRules.WorldRulesView), "TM/Modules/Design/GlobalSettings/WorldRules/WorldRulesView"),
                }),
                new SubModuleNavigation("设计元素", "Icon.Mask", new[]
                {
                    new FunctionNavigation("角色规则", "Icon.User", typeof(TM.Modules.Design.Elements.CharacterRules.CharacterRulesView), "TM/Modules/Design/Elements/CharacterRules/CharacterRulesView"),
                    new FunctionNavigation("势力规则", "Icon.Institution", typeof(TM.Modules.Design.Elements.FactionRules.FactionRulesView), "TM/Modules/Design/Elements/FactionRules/FactionRulesView"),
                    new FunctionNavigation("位置规则", "Icon.MapPin", typeof(TM.Modules.Design.Elements.LocationRules.LocationRulesView), "TM/Modules/Design/Elements/LocationRules/LocationRulesView"),
                    new FunctionNavigation("剧情规则", "Icon.Book", typeof(TM.Modules.Design.Elements.PlotRules.PlotRulesView), "TM/Modules/Design/Elements/PlotRules/PlotRulesView"),
                }),
            }
        };

        public static readonly ModuleNavigation Generate = new()
        {
            Name = "创作",
            Icon = "Icon.Sparkle",
            Type = "Generate",
            SubModules = new[]
            {
                new SubModuleNavigation("全书设定", "Icon.Book", new[]
                {
                    new FunctionNavigation("大纲设计", "Icon.Book", typeof(TM.Modules.Generate.GlobalSettings.Outline.OutlineView), "TM/Modules/Generate/GlobalSettings/Outline/OutlineView"),
                }),
                new SubModuleNavigation("创作元素", "Icon.Clapper", new[]
                {
                    new FunctionNavigation("分卷设计", "Icon.Books", typeof(TM.Modules.Generate.Elements.VolumeDesign.VolumeDesignView), "TM/Modules/Generate/Elements/VolumeDesign/VolumeDesignView"),
                    new FunctionNavigation("章节设计", "Icon.Chapter", typeof(TM.Modules.Generate.Elements.Chapter.ChapterView), "TM/Modules/Generate/Elements/Chapter/ChapterView"),
                    new FunctionNavigation("蓝图设计", "Icon.Clapper", typeof(TM.Modules.Generate.Elements.Blueprint.BlueprintView), "TM/Modules/Generate/Elements/Blueprint/BlueprintView"),
                }),
                new SubModuleNavigation("正文配置", "Icon.Document", new[]
                {
                    new FunctionNavigation("数据中心", "Icon.Package", typeof(TM.Modules.Generate.Content.ContentView), "TM/Modules/Generate/Content/ContentView"),
                    new FunctionNavigation("章节预览", "Icon.Book", typeof(TM.Modules.Generate.Content.ChapterPreview.ChapterPreviewView), "TM/Modules/Generate/Content/ChapterPreview/ChapterPreviewView"),
                }),
            }
        };

        #endregion

        #region 左侧导航树定义 - 个人模块

        public static readonly ModuleNavigation User = new()
        {
            Name = "用户",
            Icon = "Icon.User",
            Type = "User",
            SubModules = new[]
            {
                new SubModuleNavigation("用户资料", "Icon.User", new[]
                {
                    new FunctionNavigation("基本信息", "Icon.Clipboard", typeof(TM.Framework.User.Profile.BasicInfo.BasicInfoView), "TM/Framework/User/Profile/BasicInfo/BasicInfoView"),
                    new FunctionNavigation("会员订阅", "Icon.Diamond", typeof(TM.Framework.User.Profile.Subscription.SubscriptionView), "TM/Framework/User/Profile/Subscription/SubscriptionView"),
                }),
                new SubModuleNavigation("账号管理", "Icon.Lock", new[]
                {
                    new FunctionNavigation("账号绑定", "Icon.Link", typeof(TM.Framework.User.Account.AccountBinding.AccountBindingView), "TM/Framework/User/Account/AccountBinding/AccountBindingView"),
                    new FunctionNavigation("登录历史", "Icon.Chart", typeof(TM.Framework.User.Account.LoginHistory.LoginHistoryView), "TM/Framework/User/Account/LoginHistory/LoginHistoryView"),
                    new FunctionNavigation("密码安全", "Icon.Lock", typeof(TM.Framework.User.Account.PasswordSecurity.PasswordSecurityView), "TM/Framework/User/Account/PasswordSecurity/PasswordSecurityView"),
                    new FunctionNavigation("账号注销", "Icon.Trash", typeof(TM.Framework.User.Account.AccountDeletion.AccountDeletionView), "TM/Framework/User/Account/AccountDeletion/AccountDeletionView"),
                }),
                new SubModuleNavigation("安全锁定", "Icon.Lock", new[]
                {
                    new FunctionNavigation("密码锁定", "Icon.Lock", typeof(TM.Framework.User.Security.PasswordProtection.PasswordLock.PasswordLockView), "TM/Framework/User/Security/PasswordProtection/PasswordLock/PasswordLockView"),
                    new FunctionNavigation("自动锁定", "Icon.Clock", typeof(TM.Framework.User.Security.PasswordProtection.AutoLock.AutoLockView), "TM/Framework/User/Security/PasswordProtection/AutoLock/AutoLockView"),
                }),
                new SubModuleNavigation("偏好设置", "Icon.Settings", new[]
                {
                    new FunctionNavigation("显示设置", "Icon.Monitor", typeof(TM.Framework.User.Preferences.Display.DisplayView), "TM/Framework/User/Preferences/Display/DisplayView"),
                    new FunctionNavigation("语言区域", "Icon.Globe", typeof(TM.Framework.User.Preferences.Locale.LocaleView), "TM/Framework/User/Preferences/Locale/LocaleView"),
                }),
            }
        };

        public static readonly ModuleNavigation Appearance = new()
        {
            Name = "界面",
            Icon = "Icon.Palette",
            Type = "Appearance",
            SubModules = new[]
            {
                new SubModuleNavigation("主题外观", "Icon.Palette", new[]
                {
                    new FunctionNavigation("主题选择", "Icon.Palette", typeof(TM.Framework.Appearance.ThemeManagement.ThemeSelection.ThemeSelectionView), "TM/Framework/Appearance/ThemeManagement/ThemeSelection/ThemeSelectionView"),
                    new FunctionNavigation("主题设计", "Icon.Edit", typeof(TM.Framework.Appearance.ThemeManagement.ThemeDesign.ThemeDesignView), "TM/Framework/Appearance/ThemeManagement/ThemeDesign/ThemeDesignView"),
                    new FunctionNavigation("主题导入导出", "Icon.Package", typeof(TM.Framework.Appearance.ThemeManagement.ThemeImportExport.ThemeImportExportView), "TM/Framework/Appearance/ThemeManagement/ThemeImportExport/ThemeImportExportView"),
                }),
                new SubModuleNavigation("智能配色", "Icon.Robot", new[]
                {
                    new FunctionNavigation("图片取色器", "Icon.Palette", typeof(TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker.ImageColorPickerView), "TM/Framework/Appearance/IntelligentGeneration/ImageColorPicker/ImageColorPickerView"),
                    new FunctionNavigation("AI配色方案", "Icon.Sparkle", typeof(TM.Framework.Appearance.IntelligentGeneration.AIColorScheme.AIColorSchemeView), "TM/Framework/Appearance/IntelligentGeneration/AIColorScheme/AIColorSchemeView"),
                    new FunctionNavigation("生成历史", "Icon.Chart", typeof(TM.Framework.Appearance.IntelligentGeneration.GenerationHistory.GenerationHistoryView), "TM/Framework/Appearance/IntelligentGeneration/GenerationHistory/GenerationHistoryView"),
                }),
                new SubModuleNavigation("动画效果", "Icon.Clapper", new[]
                {
                    new FunctionNavigation("加载动画", "Icon.Clock", typeof(TM.Framework.Appearance.Animation.LoadingAnimation.LoadingAnimationView), "TM/Framework/Appearance/Animation/LoadingAnimation/LoadingAnimationView"),
                    new FunctionNavigation("主题过渡", "Icon.Refresh", typeof(TM.Framework.Appearance.Animation.ThemeTransition.ThemeTransitionView), "TM/Framework/Appearance/Animation/ThemeTransition/ThemeTransitionView"),
                    new FunctionNavigation("UI分辨率", "Icon.Ruler", typeof(TM.Framework.Appearance.Animation.UIResolution.UIResolutionView), "TM/Framework/Appearance/Animation/UIResolution/UIResolutionView"),
                }),
                new SubModuleNavigation("自动切换", "Icon.Moon", new[]
                {
                    new FunctionNavigation("跟随系统", "Icon.Monitor", typeof(TM.Framework.Appearance.AutoTheme.SystemFollow.SystemFollowView), "TM/Framework/Appearance/AutoTheme/SystemFollow/SystemFollowView"),
                    new FunctionNavigation("定时切换", "Icon.Clock", typeof(TM.Framework.Appearance.AutoTheme.TimeBased.TimeBasedView), "TM/Framework/Appearance/AutoTheme/TimeBased/TimeBasedView"),
                }),
                new SubModuleNavigation("字体设置", "Icon.Edit", new[]
                {
                    new FunctionNavigation("UI字体", "Icon.Font", typeof(TM.Framework.Appearance.Font.UIFont.UIFontView), "TM/Framework/Appearance/Font/UIFont/UIFontView"),
                    new FunctionNavigation("编辑器字体", "Icon.Write", typeof(TM.Framework.Appearance.Font.EditorFont.EditorFontView), "TM/Framework/Appearance/Font/EditorFont/EditorFontView"),
                }),
            }
        };

        public static readonly ModuleNavigation Notifications = new()
        {
            Name = "通知",
            Icon = "Icon.Bell",
            Type = "Notifications",
            SubModules = new[]
            {
                new SubModuleNavigation("通知设置", "Icon.Bell", new[]
                {
                    new FunctionNavigation("通知类型", "Icon.Clipboard", typeof(TM.Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypesView), "TM/Framework/Notifications/SystemNotifications/NotificationTypes/NotificationTypesView"),
                    new FunctionNavigation("通知偏好", "Icon.Palette", typeof(TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleView), "TM/Framework/Notifications/SystemNotifications/NotificationStyle/NotificationStyleView"),
                    new FunctionNavigation("系统集成", "Icon.Monitor", typeof(TM.Framework.Notifications.SystemNotifications.SystemIntegration.SystemIntegrationView), "TM/Framework/Notifications/SystemNotifications/SystemIntegration/SystemIntegrationView"),
                }),
                new SubModuleNavigation("通知管理", "Icon.Bell", new[]
                {
                    new FunctionNavigation("免打扰", "Icon.Moon", typeof(TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbView), "TM/Framework/Notifications/NotificationManagement/DoNotDisturb/DoNotDisturbView"),
                    new FunctionNavigation("通知历史", "Icon.Scroll", typeof(TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistoryView), "TM/Framework/Notifications/NotificationManagement/NotificationHistory/NotificationHistoryView"),
                }),
                new SubModuleNavigation("音效管理", "Icon.Speaker", new[]
                {
                    new FunctionNavigation("音量与设备", "Icon.Music", typeof(TM.Framework.Notifications.Sound.VolumeAndDevice.VolumeAndDeviceView), "TM/Framework/Notifications/Sound/VolumeAndDevice/VolumeAndDeviceView"),
                    new FunctionNavigation("音效方案", "Icon.Music", typeof(TM.Framework.Notifications.Sound.SoundScheme.SoundSchemeView), "TM/Framework/Notifications/Sound/SoundScheme/SoundSchemeView"),
                    new FunctionNavigation("语音播报", "Icon.Microphone", typeof(TM.Framework.Notifications.Sound.VoiceBroadcast.VoiceBroadcastView), "TM/Framework/Notifications/Sound/VoiceBroadcast/VoiceBroadcastView"),
                    new FunctionNavigation("音效库", "Icon.Music", typeof(TM.Framework.Notifications.Sound.SoundLibrary.SoundLibraryView), "TM/Framework/Notifications/Sound/SoundLibrary/SoundLibraryView"),
                }),
            }
        };

        public static readonly ModuleNavigation SystemSettings = new()
        {
            Name = "系统",
            Icon = "Icon.Settings",
            Type = "SystemSettings",
            SubModules = new[]
            {
                new SubModuleNavigation("数据管理", "Icon.Database", new[]
                {
                    new FunctionNavigation("数据清理", "Icon.Trash", typeof(TM.Framework.SystemSettings.DataCleanup.DataCleanupView), "TM/Framework/SystemSettings/DataCleanup/DataCleanupView"),
                    new FunctionNavigation("备份管理", "Icon.Download", typeof(TM.Framework.SystemSettings.DataBackup.DataBackupView), "TM/Framework/SystemSettings/DataBackup/DataBackupView"),
                }),
                new SubModuleNavigation("代理设置", "Icon.Link", new[]
                {
                    new FunctionNavigation("代理设置", "Icon.Settings", typeof(TM.Framework.SystemSettings.Proxy.ProxySetup.ProxySetupView), "TM/Framework/SystemSettings/Proxy/ProxySetup/ProxySetupView"),
                    new FunctionNavigation("代理规则", "Icon.Clipboard", typeof(TM.Framework.SystemSettings.Proxy.ProxyRules.ProxyRulesView), "TM/Framework/SystemSettings/Proxy/ProxyRules/ProxyRulesView"),
                    new FunctionNavigation("代理链", "Icon.Link", typeof(TM.Framework.SystemSettings.Proxy.ProxyChain.ProxyChainView), "TM/Framework/SystemSettings/Proxy/ProxyChain/ProxyChainView"),
                    new FunctionNavigation("代理测试", "Icon.Flask", typeof(TM.Framework.SystemSettings.Proxy.ProxyTest.ProxyTestView), "TM/Framework/SystemSettings/Proxy/ProxyTest/ProxyTestView"),
                }),
                new SubModuleNavigation("日志管理", "Icon.Clipboard", new[]
                {
                    new FunctionNavigation("日志级别", "Icon.Wrench", typeof(TM.Framework.SystemSettings.Logging.LogLevel.LogLevelView), "TM/Framework/SystemSettings/Logging/LogLevel/LogLevelView"),
                    new FunctionNavigation("日志输出", "Icon.Folder", typeof(TM.Framework.SystemSettings.Logging.LogOutput.LogOutputView), "TM/Framework/SystemSettings/Logging/LogOutput/LogOutputView"),
                    new FunctionNavigation("日志格式", "Icon.Document", typeof(TM.Framework.SystemSettings.Logging.LogFormat.LogFormatView), "TM/Framework/SystemSettings/Logging/LogFormat/LogFormatView"),
                    new FunctionNavigation("日志轮转", "Icon.Refresh", typeof(TM.Framework.SystemSettings.Logging.LogRotation.LogRotationView), "TM/Framework/SystemSettings/Logging/LogRotation/LogRotationView"),
                }),
                new SubModuleNavigation("系统信息", "Icon.Info", new[]
                {
                    new FunctionNavigation("应用信息", "Icon.Smartphone", typeof(TM.Framework.SystemSettings.Info.AppInfo.AppInfoView), "TM/Framework/SystemSettings/Info/AppInfo/AppInfoView"),
                    new FunctionNavigation("系统信息", "Icon.Monitor", typeof(TM.Framework.SystemSettings.Info.SystemInfo.SystemInfoView), "TM/Framework/SystemSettings/Info/SystemInfo/SystemInfoView"),
                    new FunctionNavigation("运行环境", "Icon.Globe", typeof(TM.Framework.SystemSettings.Info.RuntimeEnv.RuntimeEnvView), "TM/Framework/SystemSettings/Info/RuntimeEnv/RuntimeEnvView"),
                    new FunctionNavigation("诊断信息", "Icon.Wrench", typeof(TM.Framework.SystemSettings.Info.DiagnosticInfo.DiagnosticInfoView), "TM/Framework/SystemSettings/Info/DiagnosticInfo/DiagnosticInfoView"),
                    new FunctionNavigation("系统监控", "Icon.Chart", typeof(TM.Framework.SystemSettings.Info.SystemMonitor.SystemMonitorView), "TM/Framework/SystemSettings/Info/SystemMonitor/SystemMonitorView"),
                }),
            }
        };

        #endregion

        #region 辅助方法

        public static string? GetFunctionViewPath(string moduleName, string subModuleName)
        {
            var module = GetModuleByName(moduleName);
            if (module == null) return null;

            var subModule = module.SubModules.FirstOrDefault(sm =>
                sm.Name.Equals(subModuleName, StringComparison.OrdinalIgnoreCase));

            if (subModule == null || subModule.Functions.Length == 0)
                return null;

            return subModule.Functions[0].ViewPath;
        }

        public static string? GetFunctionViewPath(string moduleName, string subModuleName, string functionName)
        {
            var module = GetModuleByName(moduleName);
            if (module == null) return null;

            var subModule = module.SubModules.FirstOrDefault(sm =>
                sm.Name.Equals(subModuleName, StringComparison.OrdinalIgnoreCase));

            if (subModule == null || subModule.Functions.Length == 0)
                return null;

            var func = subModule.Functions.FirstOrDefault(f =>
                f.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase));

            return func?.ViewPath;
        }

        public static Type? GetFunctionViewType(string moduleName, string subModuleName)
        {
            var module = GetModuleByName(moduleName);
            if (module == null) return null;

            var subModule = module.SubModules.FirstOrDefault(sm =>
                sm.Name.Equals(subModuleName, StringComparison.OrdinalIgnoreCase));

            if (subModule == null || subModule.Functions.Length == 0)
                return null;

            return subModule.Functions[0].ViewType;
        }

        public static Type? GetFunctionViewType(string moduleName, string subModuleName, string functionName)
        {
            var module = GetModuleByName(moduleName);
            if (module == null) return null;

            var subModule = module.SubModules.FirstOrDefault(sm =>
                sm.Name.Equals(subModuleName, StringComparison.OrdinalIgnoreCase));

            if (subModule == null || subModule.Functions.Length == 0)
                return null;

            var func = subModule.Functions.FirstOrDefault(f =>
                f.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase));

            return func?.ViewType;
        }

        public static ModuleNavigation[] GetWritingModules() => new[]
        {
            SmartAssistant, Validate, Design, Generate
        };

        public static ModuleNavigation[] GetPersonalModules() => new[]
        {
            User, Appearance, Notifications, SystemSettings
        };

        public static ModuleNavigation[] GetAllModules() => new[]
        {
            Design, Generate, SmartAssistant, Validate, User, Appearance, Notifications, SystemSettings
        };

        public static IEnumerable<Type> GetAllViewTypes()
        {
            foreach (var module in GetAllModules())
                foreach (var sub in module.SubModules)
                    foreach (var func in sub.Functions)
                        yield return func.ViewType;
        }

        public static ModuleNavigation? GetModuleByName(string moduleName)
        {
            return moduleName switch
            {
                "SmartAssistant" => SmartAssistant,
                "Validate" => Validate,
                "Design" => Design,
                "Generate" => Generate,
                "User" => User,
                "Appearance" => Appearance,
                "Notifications" => Notifications,
                "SystemSettings" => SystemSettings,
                _ => null
            };
        }

        #endregion
    }

    #region 数据模型

    public record TabDefinition(int Index, string Icon, string Title, string ModuleName);

    public class ModuleNavigation
    {
        public string Name { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Type { get; init; } = "";
        public SubModuleNavigation[] SubModules { get; init; } = System.Array.Empty<SubModuleNavigation>();
    }

    public class SubModuleNavigation
    {
        public string Name { get; }
        public string Icon { get; }
        public FunctionNavigation[] Functions { get; }

        public SubModuleNavigation(string name, string icon, FunctionNavigation[] functions)
        {
            Name = name;
            Icon = icon;
            Functions = functions;
        }
    }

    public class FunctionNavigation
    {
        public string Name { get; }
        public string Icon { get; }
        public string ViewPath { get; }
        public Type ViewType { get; }

        public FunctionNavigation(string name, string icon, Type viewType, string viewPath)
        {
            Name = name;
            Icon = icon;
            ViewType = viewType;
            ViewPath = viewPath;
        }
    }

    #endregion
}
