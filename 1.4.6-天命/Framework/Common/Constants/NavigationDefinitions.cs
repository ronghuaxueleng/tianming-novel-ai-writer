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
            new TabDefinition(0, "✏️", "设计", "Design"),
            new TabDefinition(1, "✍️", "创作", "Generate"),
            new TabDefinition(2, "🔍", "校验", "Validate"),
            new TabDefinition(3, "🤖", "智助", "SmartAssistant"),
        };

        public static readonly TabDefinition[] PersonalTabs = new[]
        {
            new TabDefinition(0, "👤", "用户", "User"),
            new TabDefinition(1, "🎨", "界面", "Appearance"),
            new TabDefinition(2, "🔔", "通知", "Notifications"),
            new TabDefinition(3, "⚙️", "系统", "SystemSettings"),
        };

        #endregion

        #region 左侧导航树定义 - 写作模块

        public static readonly ModuleNavigation SmartAssistant = new()
        {
            Name = "AI助手",
            Icon = "🤖",
            Type = "SmartAssistant",
            SubModules = new[]
            {
                new SubModuleNavigation("模型集成", "📊", new[]
                {
                    new FunctionNavigation("模型管理", "🤖", typeof(TM.Modules.AIAssistant.ModelIntegration.ModelManagement.ModelManagementView), "TM/Modules/AIAssistant/ModelIntegration/ModelManagement/ModelManagementView"),
                    new FunctionNavigation("API统计", "📈", typeof(TM.Modules.AIAssistant.ModelIntegration.UsageStatistics.UsageStatisticsView), "TM/Modules/AIAssistant/ModelIntegration/UsageStatistics/UsageStatisticsView"),
                }),
                new SubModuleNavigation("提示词工具", "💬", new[]
                {
                    new FunctionNavigation("提示词管理", "📚", typeof(TM.Modules.AIAssistant.PromptTools.PromptManagement.PromptManagementView), "TM/Modules/AIAssistant/PromptTools/PromptManagement/PromptManagementView"),
                    new FunctionNavigation("版本测试", "🧪", typeof(TM.Modules.AIAssistant.PromptTools.VersionTesting.VersionTestingView), "TM/Modules/AIAssistant/PromptTools/VersionTesting/VersionTestingView"),
                }),
                new SubModuleNavigation("对话增强", "🧠", new[]
                {
                    new FunctionNavigation("记忆管理", "🧠", typeof(TM.Modules.AIAssistant.MemoryManagement.MemoryManagementView), "TM/Modules/AIAssistant/MemoryManagement/MemoryManagementView"),
                }),
            }
        };

        public static readonly ModuleNavigation Validate = new()
        {
            Name = "校验",
            Icon = "🔍",
            Type = "Validate",
            SubModules = new[]
            {
                new SubModuleNavigation("校验汇总", "📊", new[]
                {
                    new FunctionNavigation("校验结果", "📋", typeof(TM.Modules.Validate.ValidationSummary.ValidationResult.ValidationResultView), "TM/Modules/Validate/ValidationSummary/ValidationResult/ValidationResultView"),
                }),
                new SubModuleNavigation("校验介绍", "📖", new[]
                {
                    new FunctionNavigation("世界观校验", "🌍", typeof(TM.Modules.Validate.ValidationIntro.WorldviewIntro.WorldviewIntroView), "TM/Modules/Validate/ValidationIntro/WorldviewIntro/WorldviewIntroView"),
                    new FunctionNavigation("角色校验", "👤", typeof(TM.Modules.Validate.ValidationIntro.CharacterIntro.CharacterIntroView), "TM/Modules/Validate/ValidationIntro/CharacterIntro/CharacterIntroView"),
                    new FunctionNavigation("剧情校验", "📖", typeof(TM.Modules.Validate.ValidationIntro.PlotIntro.PlotIntroView), "TM/Modules/Validate/ValidationIntro/PlotIntro/PlotIntroView"),
                    new FunctionNavigation("大纲校验", "📝", typeof(TM.Modules.Validate.ValidationIntro.OutlineIntro.OutlineIntroView), "TM/Modules/Validate/ValidationIntro/OutlineIntro/OutlineIntroView"),
                    new FunctionNavigation("章节校验", "📑", typeof(TM.Modules.Validate.ValidationIntro.ChapterIntro.ChapterIntroView), "TM/Modules/Validate/ValidationIntro/ChapterIntro/ChapterIntroView"),
                    new FunctionNavigation("正文校验", "📄", typeof(TM.Modules.Validate.ValidationIntro.ContentIntro.ContentIntroView), "TM/Modules/Validate/ValidationIntro/ContentIntro/ContentIntroView"),
                }),
            }
        };

        public static readonly ModuleNavigation Design = new()
        {
            Name = "设计",
            Icon = "✏️",
            Type = "Design",
            SubModules = new[]
            {
                new SubModuleNavigation("智能拆书", "🤖", new[]
                {
                    new FunctionNavigation("拆书分析", "🤖", typeof(TM.Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView), "TM/Modules/Design/SmartParsing/BookAnalysis/BookAnalysisView"),
                }),
                new SubModuleNavigation("创作模板", "💡", new[]
                {
                    new FunctionNavigation("模板管理", "💡", typeof(TM.Modules.Design.Templates.CreativeMaterials.CreativeMaterialsView), "TM/Modules/Design/Templates/CreativeMaterials/CreativeMaterialsView"),
                }),
                new SubModuleNavigation("全局设定", "🌍", new[]
                {
                    new FunctionNavigation("世界观规则", "🌍", typeof(TM.Modules.Design.GlobalSettings.WorldRules.WorldRulesView), "TM/Modules/Design/GlobalSettings/WorldRules/WorldRulesView"),
                }),
                new SubModuleNavigation("设计元素", "🎭", new[]
                {
                    new FunctionNavigation("角色规则", "👤", typeof(TM.Modules.Design.Elements.CharacterRules.CharacterRulesView), "TM/Modules/Design/Elements/CharacterRules/CharacterRulesView"),
                    new FunctionNavigation("势力规则", "🏛️", typeof(TM.Modules.Design.Elements.FactionRules.FactionRulesView), "TM/Modules/Design/Elements/FactionRules/FactionRulesView"),
                    new FunctionNavigation("位置规则", "📍", typeof(TM.Modules.Design.Elements.LocationRules.LocationRulesView), "TM/Modules/Design/Elements/LocationRules/LocationRulesView"),
                    new FunctionNavigation("剧情规则", "📖", typeof(TM.Modules.Design.Elements.PlotRules.PlotRulesView), "TM/Modules/Design/Elements/PlotRules/PlotRulesView"),
                }),
            }
        };

        public static readonly ModuleNavigation Generate = new()
        {
            Name = "创作",
            Icon = "📝",
            Type = "Generate",
            SubModules = new[]
            {
                new SubModuleNavigation("全书设定", "📖", new[]
                {
                    new FunctionNavigation("大纲设计", "📖", typeof(TM.Modules.Generate.GlobalSettings.Outline.OutlineView), "TM/Modules/Generate/GlobalSettings/Outline/OutlineView"),
                }),
                new SubModuleNavigation("创作元素", "🎬", new[]
                {
                    new FunctionNavigation("分卷设计", "📚", typeof(TM.Modules.Generate.Elements.VolumeDesign.VolumeDesignView), "TM/Modules/Generate/Elements/VolumeDesign/VolumeDesignView"),
                    new FunctionNavigation("章节设计", "📑", typeof(TM.Modules.Generate.Elements.Chapter.ChapterView), "TM/Modules/Generate/Elements/Chapter/ChapterView"),
                    new FunctionNavigation("蓝图设计", "🎬", typeof(TM.Modules.Generate.Elements.Blueprint.BlueprintView), "TM/Modules/Generate/Elements/Blueprint/BlueprintView"),
                }),
                new SubModuleNavigation("正文配置", "📄", new[]
                {
                    new FunctionNavigation("数据中心", "📦", typeof(TM.Modules.Generate.Content.ContentView), "TM/Modules/Generate/Content/ContentView"),
                    new FunctionNavigation("章节预览", "📖", typeof(TM.Modules.Generate.Content.ChapterPreview.ChapterPreviewView), "TM/Modules/Generate/Content/ChapterPreview/ChapterPreviewView"),
                }),
            }
        };

        #endregion

        #region 左侧导航树定义 - 个人模块

        public static readonly ModuleNavigation User = new()
        {
            Name = "用户",
            Icon = "👤",
            Type = "User",
            SubModules = new[]
            {
                new SubModuleNavigation("用户资料", "👤", new[]
                {
                    new FunctionNavigation("基本信息", "📋", typeof(TM.Framework.User.Profile.BasicInfo.BasicInfoView), "TM/Framework/User/Profile/BasicInfo/BasicInfoView"),
                    new FunctionNavigation("会员订阅", "💎", typeof(TM.Framework.User.Profile.Subscription.SubscriptionView), "TM/Framework/User/Profile/Subscription/SubscriptionView"),
                }),
                new SubModuleNavigation("账号管理", "🔐", new[]
                {
                    new FunctionNavigation("账号绑定", "🔗", typeof(TM.Framework.User.Account.AccountBinding.AccountBindingView), "TM/Framework/User/Account/AccountBinding/AccountBindingView"),
                    new FunctionNavigation("登录历史", "📊", typeof(TM.Framework.User.Account.LoginHistory.LoginHistoryView), "TM/Framework/User/Account/LoginHistory/LoginHistoryView"),
                    new FunctionNavigation("密码安全", "🔒", typeof(TM.Framework.User.Account.PasswordSecurity.PasswordSecurityView), "TM/Framework/User/Account/PasswordSecurity/PasswordSecurityView"),
                    new FunctionNavigation("账号注销", "🗑️", typeof(TM.Framework.User.Account.AccountDeletion.AccountDeletionView), "TM/Framework/User/Account/AccountDeletion/AccountDeletionView"),
                }),
                new SubModuleNavigation("安全锁定", "🔐", new[]
                {
                    new FunctionNavigation("密码锁定", "🔒", typeof(TM.Framework.User.Security.PasswordProtection.PasswordLock.PasswordLockView), "TM/Framework/User/Security/PasswordProtection/PasswordLock/PasswordLockView"),
                    new FunctionNavigation("自动锁定", "⏰", typeof(TM.Framework.User.Security.PasswordProtection.AutoLock.AutoLockView), "TM/Framework/User/Security/PasswordProtection/AutoLock/AutoLockView"),
                }),
                new SubModuleNavigation("偏好设置", "⚙️", new[]
                {
                    new FunctionNavigation("显示设置", "🖥️", typeof(TM.Framework.User.Preferences.Display.DisplayView), "TM/Framework/User/Preferences/Display/DisplayView"),
                    new FunctionNavigation("语言区域", "🌐", typeof(TM.Framework.User.Preferences.Locale.LocaleView), "TM/Framework/User/Preferences/Locale/LocaleView"),
                }),
            }
        };

        public static readonly ModuleNavigation Appearance = new()
        {
            Name = "界面",
            Icon = "🎨",
            Type = "Appearance",
            SubModules = new[]
            {
                new SubModuleNavigation("主题外观", "🎨", new[]
                {
                    new FunctionNavigation("主题选择", "🎨", typeof(TM.Framework.Appearance.ThemeManagement.ThemeSelection.ThemeSelectionView), "TM/Framework/Appearance/ThemeManagement/ThemeSelection/ThemeSelectionView"),
                    new FunctionNavigation("主题设计", "✏️", typeof(TM.Framework.Appearance.ThemeManagement.ThemeDesign.ThemeDesignView), "TM/Framework/Appearance/ThemeManagement/ThemeDesign/ThemeDesignView"),
                    new FunctionNavigation("主题导入导出", "📦", typeof(TM.Framework.Appearance.ThemeManagement.ThemeImportExport.ThemeImportExportView), "TM/Framework/Appearance/ThemeManagement/ThemeImportExport/ThemeImportExportView"),
                }),
                new SubModuleNavigation("智能配色", "🤖", new[]
                {
                    new FunctionNavigation("图片取色器", "🎨", typeof(TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker.ImageColorPickerView), "TM/Framework/Appearance/IntelligentGeneration/ImageColorPicker/ImageColorPickerView"),
                    new FunctionNavigation("AI配色方案", "✨", typeof(TM.Framework.Appearance.IntelligentGeneration.AIColorScheme.AIColorSchemeView), "TM/Framework/Appearance/IntelligentGeneration/AIColorScheme/AIColorSchemeView"),
                    new FunctionNavigation("生成历史", "📊", typeof(TM.Framework.Appearance.IntelligentGeneration.GenerationHistory.GenerationHistoryView), "TM/Framework/Appearance/IntelligentGeneration/GenerationHistory/GenerationHistoryView"),
                }),
                new SubModuleNavigation("动画效果", "🎬", new[]
                {
                    new FunctionNavigation("加载动画", "⏳", typeof(TM.Framework.Appearance.Animation.LoadingAnimation.LoadingAnimationView), "TM/Framework/Appearance/Animation/LoadingAnimation/LoadingAnimationView"),
                    new FunctionNavigation("主题过渡", "🔄", typeof(TM.Framework.Appearance.Animation.ThemeTransition.ThemeTransitionView), "TM/Framework/Appearance/Animation/ThemeTransition/ThemeTransitionView"),
                    new FunctionNavigation("UI分辨率", "📐", typeof(TM.Framework.Appearance.Animation.UIResolution.UIResolutionView), "TM/Framework/Appearance/Animation/UIResolution/UIResolutionView"),
                }),
                new SubModuleNavigation("自动切换", "🌙", new[]
                {
                    new FunctionNavigation("跟随系统", "💻", typeof(TM.Framework.Appearance.AutoTheme.SystemFollow.SystemFollowView), "TM/Framework/Appearance/AutoTheme/SystemFollow/SystemFollowView"),
                    new FunctionNavigation("定时切换", "🕐", typeof(TM.Framework.Appearance.AutoTheme.TimeBased.TimeBasedView), "TM/Framework/Appearance/AutoTheme/TimeBased/TimeBasedView"),
                }),
                new SubModuleNavigation("字体设置", "📝", new[]
                {
                    new FunctionNavigation("UI字体", "🖋️", typeof(TM.Framework.Appearance.Font.UIFont.UIFontView), "TM/Framework/Appearance/Font/UIFont/UIFontView"),
                    new FunctionNavigation("编辑器字体", "✍️", typeof(TM.Framework.Appearance.Font.EditorFont.EditorFontView), "TM/Framework/Appearance/Font/EditorFont/EditorFontView"),
                }),
            }
        };

        public static readonly ModuleNavigation Notifications = new()
        {
            Name = "通知",
            Icon = "🔔",
            Type = "Notifications",
            SubModules = new[]
            {
                new SubModuleNavigation("通知设置", "🔔", new[]
                {
                    new FunctionNavigation("通知类型", "📋", typeof(TM.Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypesView), "TM/Framework/Notifications/SystemNotifications/NotificationTypes/NotificationTypesView"),
                    new FunctionNavigation("通知偏好", "🎨", typeof(TM.Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleView), "TM/Framework/Notifications/SystemNotifications/NotificationStyle/NotificationStyleView"),
                    new FunctionNavigation("系统集成", "💻", typeof(TM.Framework.Notifications.SystemNotifications.SystemIntegration.SystemIntegrationView), "TM/Framework/Notifications/SystemNotifications/SystemIntegration/SystemIntegrationView"),
                }),
                new SubModuleNavigation("通知管理", "🔔", new[]
                {
                    new FunctionNavigation("免打扰", "🌙", typeof(TM.Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbView), "TM/Framework/Notifications/NotificationManagement/DoNotDisturb/DoNotDisturbView"),
                    new FunctionNavigation("通知历史", "📜", typeof(TM.Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistoryView), "TM/Framework/Notifications/NotificationManagement/NotificationHistory/NotificationHistoryView"),
                }),
                new SubModuleNavigation("音效管理", "🔊", new[]
                {
                    new FunctionNavigation("音量与设备", "🎵", typeof(TM.Framework.Notifications.Sound.VolumeAndDevice.VolumeAndDeviceView), "TM/Framework/Notifications/Sound/VolumeAndDevice/VolumeAndDeviceView"),
                    new FunctionNavigation("音效方案", "🎹", typeof(TM.Framework.Notifications.Sound.SoundScheme.SoundSchemeView), "TM/Framework/Notifications/Sound/SoundScheme/SoundSchemeView"),
                    new FunctionNavigation("语音播报", "🎤", typeof(TM.Framework.Notifications.Sound.VoiceBroadcast.VoiceBroadcastView), "TM/Framework/Notifications/Sound/VoiceBroadcast/VoiceBroadcastView"),
                    new FunctionNavigation("音效库", "🎵", typeof(TM.Framework.Notifications.Sound.SoundLibrary.SoundLibraryView), "TM/Framework/Notifications/Sound/SoundLibrary/SoundLibraryView"),
                }),
            }
        };

        public static readonly ModuleNavigation SystemSettings = new()
        {
            Name = "系统",
            Icon = "⚙️",
            Type = "SystemSettings",
            SubModules = new[]
            {
                new SubModuleNavigation("数据清理", "🗑️", new[]
                {
                    new FunctionNavigation("数据清理", "🗑️", typeof(TM.Framework.SystemSettings.DataCleanup.DataCleanupView), "TM/Framework/SystemSettings/DataCleanup/DataCleanupView"),
                }),
                new SubModuleNavigation("代理设置", "🔗", new[]
                {
                    new FunctionNavigation("代理设置", "⚙️", typeof(TM.Framework.SystemSettings.Proxy.ProxySetup.ProxySetupView), "TM/Framework/SystemSettings/Proxy/ProxySetup/ProxySetupView"),
                    new FunctionNavigation("代理规则", "📋", typeof(TM.Framework.SystemSettings.Proxy.ProxyRules.ProxyRulesView), "TM/Framework/SystemSettings/Proxy/ProxyRules/ProxyRulesView"),
                    new FunctionNavigation("代理链", "🔗", typeof(TM.Framework.SystemSettings.Proxy.ProxyChain.ProxyChainView), "TM/Framework/SystemSettings/Proxy/ProxyChain/ProxyChainView"),
                    new FunctionNavigation("代理测试", "🧪", typeof(TM.Framework.SystemSettings.Proxy.ProxyTest.ProxyTestView), "TM/Framework/SystemSettings/Proxy/ProxyTest/ProxyTestView"),
                }),
                new SubModuleNavigation("日志管理", "📋", new[]
                {
                    new FunctionNavigation("日志级别", "🔧", typeof(TM.Framework.SystemSettings.Logging.LogLevel.LogLevelView), "TM/Framework/SystemSettings/Logging/LogLevel/LogLevelView"),
                    new FunctionNavigation("日志输出", "📁", typeof(TM.Framework.SystemSettings.Logging.LogOutput.LogOutputView), "TM/Framework/SystemSettings/Logging/LogOutput/LogOutputView"),
                    new FunctionNavigation("日志格式", "📄", typeof(TM.Framework.SystemSettings.Logging.LogFormat.LogFormatView), "TM/Framework/SystemSettings/Logging/LogFormat/LogFormatView"),
                    new FunctionNavigation("日志轮转", "🔄", typeof(TM.Framework.SystemSettings.Logging.LogRotation.LogRotationView), "TM/Framework/SystemSettings/Logging/LogRotation/LogRotationView"),
                }),
                new SubModuleNavigation("系统信息", "ℹ️", new[]
                {
                    new FunctionNavigation("应用信息", "📱", typeof(TM.Framework.SystemSettings.Info.AppInfo.AppInfoView), "TM/Framework/SystemSettings/Info/AppInfo/AppInfoView"),
                    new FunctionNavigation("系统信息", "💻", typeof(TM.Framework.SystemSettings.Info.SystemInfo.SystemInfoView), "TM/Framework/SystemSettings/Info/SystemInfo/SystemInfoView"),
                    new FunctionNavigation("运行环境", "🌍", typeof(TM.Framework.SystemSettings.Info.RuntimeEnv.RuntimeEnvView), "TM/Framework/SystemSettings/Info/RuntimeEnv/RuntimeEnvView"),
                    new FunctionNavigation("诊断信息", "🔧", typeof(TM.Framework.SystemSettings.Info.DiagnosticInfo.DiagnosticInfoView), "TM/Framework/SystemSettings/Info/DiagnosticInfo/DiagnosticInfoView"),
                    new FunctionNavigation("系统监控", "📊", typeof(TM.Framework.SystemSettings.Info.SystemMonitor.SystemMonitorView), "TM/Framework/SystemSettings/Info/SystemMonitor/SystemMonitorView"),
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
            SmartAssistant, Validate, Design, Generate, User, Appearance, Notifications, SystemSettings
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
