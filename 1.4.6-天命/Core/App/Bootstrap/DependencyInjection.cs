using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TM.Framework.Common.Services;
using TM.Framework.Common.Services.Factories;
using TM.Framework.User.Account.Login;

namespace TM
{
    public static class DependencyInjection
    {
        private static IServiceProvider? _serviceProvider;

        public static IServiceProvider ConfigureServices()
        {
            if (_serviceProvider != null)
                return _serviceProvider;

            var services = new ServiceCollection();

            services.AddSingleton<IWindowFactory, WindowFactory>();
            services.AddSingleton<IViewFactory, ViewFactory>();
            services.AddSingleton<IObjectFactory, TM.Framework.Common.Services.Factories.ObjectFactory>();
            services.AddSingleton<IStoragePathHelper, StoragePathHelperService>();

            services.AddTransient<LoginWindow>();
            services.AddTransient<Framework.User.Account.Login.SplashWindow>();
            services.AddTransient<MainWindow>();

            services.AddSingleton<Services.Modules.ProjectData.Implementations.GuideContextService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IWorkScopeService, 
                Services.Modules.ProjectData.Implementations.WorkScopeService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IFocusContextService, 
                Services.Modules.ProjectData.Implementations.FocusContextService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IChangeDetectionService, 
                Services.Modules.ProjectData.Implementations.ChangeDetectionService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IPublishService, 
                Services.Modules.ProjectData.Implementations.PublishService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IPackageHistoryService,
                Services.Modules.ProjectData.Implementations.PackageHistoryService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IModuleEnabledService, 
                Services.Modules.ProjectData.Implementations.ModuleEnabledService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ValidationSummaryService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IValidationSummaryService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.ValidationSummaryService>());

            services.AddSingleton<Modules.AIAssistant.PromptTools.PromptManagement.Services.PromptService>();
            services.AddSingleton<Services.Framework.AI.Interfaces.Prompts.IPromptRepository>(sp =>
                sp.GetRequiredService<Modules.AIAssistant.PromptTools.PromptManagement.Services.PromptService>());
            services.AddSingleton<Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService>();
            services.AddSingleton<Modules.AIAssistant.PromptTools.VersionTesting.Services.VersionTestingService>();

            services.AddSingleton<Modules.Design.SmartParsing.BookAnalysis.Services.BookAnalysisService>();
            services.AddSingleton<Modules.Design.Templates.CreativeMaterials.Services.CreativeMaterialsService>();
            services.AddSingleton<Modules.Design.GlobalSettings.WorldRules.Services.WorldRulesService>();
            services.AddSingleton<Modules.Design.Elements.CharacterRules.Services.CharacterRulesService>();
            services.AddSingleton<Modules.Design.Elements.FactionRules.Services.FactionRulesService>();
            services.AddSingleton<Modules.Design.Elements.LocationRules.Services.LocationRulesService>();
            services.AddSingleton<Modules.Design.Elements.PlotRules.Services.PlotRulesService>();

            services.AddSingleton<Modules.Validate.ValidationSummary.ValidationResult.ChapterRepairService>();

            services.AddSingleton<Modules.Generate.GlobalSettings.Outline.Services.OutlineService>();
            services.AddSingleton<Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
            services.AddSingleton<Modules.Generate.Elements.Chapter.Services.ChapterService>();
            services.AddSingleton<Modules.Generate.Elements.Blueprint.Services.BlueprintService>();

            services.AddSingleton<Framework.Appearance.Animation.LoadingAnimation.LoadingAnimationService>();
            services.AddSingleton<Framework.Common.Services.ServerAuthService>();
            services.AddSingleton<Framework.Common.Services.UIStateCache>();
            services.AddSingleton<Framework.Common.Services.MemoryOptimizationService>();
            services.AddSingleton<Framework.SystemSettings.Proxy.Services.ProxyRuleService>();
            services.AddSingleton<Framework.SystemSettings.Proxy.Services.ProxyService>();
            services.AddSingleton<Framework.SystemSettings.Proxy.Services.ProxyTestService>();
            services.AddSingleton<Framework.UI.Workspace.Services.PanelCommunicationService>();
            services.AddSingleton<Framework.UI.Workspace.Services.ProjectManager>();
            services.AddSingleton<Framework.UI.Workspace.Services.CurrentChapterPersistenceService>();
            services.AddSingleton<Framework.UI.Workspace.Services.Spec.SpecLoader>();
            services.AddSingleton<Framework.UI.Workspace.Services.TodoExecutionService>();
            services.AddSingleton<Framework.User.Account.AccountBinding.AccountBindingService>();
            services.AddSingleton<Framework.User.Account.Login.LoginService>();
            services.AddSingleton<Framework.User.Account.LoginHistory.LoginHistoryService>();
            services.AddSingleton<Framework.User.Account.PasswordSecurity.PasswordSecuritySettings>();
            services.AddSingleton<Framework.User.Account.PasswordSecurity.Services.AccountLockoutService>();
            services.AddSingleton<Framework.User.Account.PasswordSecurity.Services.AccountSecurityService>();
            services.AddSingleton<Framework.User.Preferences.Display.DisplayService>();
            services.AddSingleton<Framework.User.Preferences.Locale.LocaleService>();
            services.AddSingleton<Framework.User.Security.PasswordProtection.AppLockSettings>();
            services.AddSingleton<Framework.User.Services.ApiService>();
            services.AddSingleton<Framework.User.Services.AuthTokenManager>();
            services.AddSingleton<Framework.User.Services.OAuthService>();
            services.AddSingleton<Framework.User.Services.SubscriptionService>();
            services.AddSingleton<Framework.SystemSettings.Info.SystemInfo.SystemInfoSettings>();
            services.AddSingleton<Services.Framework.AI.Core.ApiKeyRotationService>();
            services.AddSingleton<Services.Framework.AI.Core.AIService>();
            services.AddSingleton<Services.Framework.AI.Interfaces.AI.IAITextGenerationService>(sp =>
                sp.GetRequiredService<Services.Framework.AI.Core.AIService>());
            services.AddSingleton<Services.Framework.AI.Interfaces.AI.IAIConfigurationService>(sp =>
                sp.GetRequiredService<Services.Framework.AI.Core.AIService>());
            services.AddSingleton<Services.Framework.AI.Interfaces.AI.IAILibraryService>(sp =>
                sp.GetRequiredService<Services.Framework.AI.Core.AIService>());
            services.AddSingleton<Services.Framework.AI.Monitoring.StatisticsService>();
            services.AddSingleton<Services.Framework.AI.Interfaces.AI.IAIUsageStatisticsService>(sp =>
                sp.GetRequiredService<Services.Framework.AI.Monitoring.StatisticsService>());
            services.AddSingleton<Services.Framework.AI.SemanticKernel.ChatPromptBridge>();
            services.AddSingleton<Services.Framework.AI.SemanticKernel.Plugins.AutoRewriteEngine>();
            services.AddSingleton<Services.Framework.AI.SemanticKernel.Plugins.LayeredPromptBuilder>();
            services.AddSingleton<Services.Framework.AI.QueryRouting.QueryRouter>();
            services.AddSingleton<Services.Framework.AI.QueryRouting.QueryRoutingService>();
            services.AddSingleton<Services.Framework.AI.SemanticKernel.SessionManager>();
            services.AddSingleton<Services.Framework.AI.SemanticKernel.SKChatService>();
            services.AddSingleton<Services.Framework.AI.SemanticKernel.VectorSearchService>();
            services.AddSingleton<Services.Framework.SystemIntegration.GlobalCleanupService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.CharacterStateService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ConflictProgressService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.DataIndexService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.FactSnapshotExtractor>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ForeshadowingStatusService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.GenerationGate>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.Generation.ContentPolisher>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.GenerationStatisticsService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.GuideManager>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ChapterSummaryStore>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ChapterMilestoneStore>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.VolumeFactArchiveStore>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.KeywordChapterIndexService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.LedgerConsistencyChecker>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.Tracking.Rules.LedgerRuleSetProvider>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.PlotPointsIndexService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.LocationStateService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.FactionStateService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.TimelineService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ItemStateService>();
            services.AddSingleton<Services.Modules.VersionTracking.VersionTrackingService>();

            services.AddSingleton<Framework.Appearance.Animation.ThemeTransition.ThemeTransitionService>();
            services.AddSingleton<Framework.Appearance.Animation.UIResolution.UIResolutionService>();
            services.AddSingleton<Framework.Appearance.AutoTheme.ConflictResolver>();
            services.AddSingleton<Framework.Appearance.AutoTheme.SystemFollow.SceneDetector>();
            services.AddSingleton<Framework.Appearance.AutoTheme.SystemFollow.SystemFollowController>();
            services.AddSingleton<Framework.Appearance.AutoTheme.SystemFollow.SystemThemeMonitor>();
            services.AddSingleton<Framework.Appearance.AutoTheme.TimeBased.HolidayLibrary>();
            services.AddSingleton<Framework.Appearance.AutoTheme.TimeBased.TimeScheduleService>();
            services.AddSingleton<Framework.Appearance.Font.Services.CharWidthAnalyzer>();
            services.AddSingleton<Framework.Appearance.Font.Services.CodeSampleProvider>();
            services.AddSingleton<Framework.Appearance.Font.Services.EditorFontPresetService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontCategoryService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontFallbackService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontFavoriteService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontImportExportService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontInfoService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontPerformanceAnalyzer>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontPresetService>();
            services.AddSingleton<Framework.Appearance.Font.Services.FontThemeMatchService>();
            services.AddSingleton<Framework.Appearance.Font.Services.LigatureDetector>();
            services.AddSingleton<Framework.Appearance.Font.Services.MonospaceFontDetector>();
            services.AddSingleton<Framework.Appearance.Font.Services.OpenTypeFeaturesService>();
            services.AddSingleton<Framework.Appearance.Font.Services.ScenePresetService>();
            services.AddSingleton<Framework.Appearance.ThemeManagement.ThemeManager>();
            services.AddSingleton<Framework.Common.Services.StandardFormOptionsService>();
            services.AddSingleton<Framework.Notifications.Sound.Services.AudioEqualizerService>();
            services.AddSingleton<Framework.Notifications.Sound.VolumeAndDevice.AudioDeviceManager>();
            services.AddSingleton<Framework.Notifications.Sound.VolumeAndDevice.SystemVolumeController>();
            services.AddSingleton<Framework.User.Profile.BasicInfo.UserProfileService>();
            services.AddSingleton<Framework.User.Services.CurrentUserContext>();
            services.AddSingleton<Services.Framework.Notification.NotificationSoundService>();
            services.AddSingleton<Services.Framework.Settings.LogManager>();
            services.AddSingleton<Services.Framework.Settings.SettingsManager>();
            services.AddSingleton<Services.Framework.SystemIntegration.TrayIconService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.SessionContextCache>();

            services.AddSingleton<Framework.User.Preferences.Locale.LocaleSettings>();
            services.AddSingleton<Framework.User.Preferences.Display.DisplaySettings>();
            services.AddSingleton<Framework.User.Profile.BasicInfo.BasicInfoSettings>();
            services.AddSingleton<Framework.Notifications.SystemNotifications.SystemIntegration.SystemIntegrationSettings>();
            services.AddSingleton<Framework.Appearance.ThemeManagement.ThemeSelection.ThemeSelectionSettings>();
            services.AddSingleton<Framework.Appearance.IntelligentGeneration.GenerationHistory.GenerationHistorySettings>();
            services.AddSingleton<Framework.User.Account.LoginHistory.LoginHistorySettings>();
            services.AddSingleton<Framework.Appearance.Font.FontConfigurationSettings>();
            services.AddSingleton<Framework.User.Account.AccountBinding.AccountBindingSettings>();
            services.AddSingleton<Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypeSettings>();
            services.AddSingleton<Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleSettings>();
            services.AddSingleton<Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbSettings>();
            services.AddSingleton<Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistorySettings>();
            services.AddSingleton<Framework.Notifications.Sound.VoiceBroadcast.VoiceBroadcastSettings>();
            services.AddSingleton<Framework.Notifications.Sound.VolumeAndDevice.VolumeAndDeviceSettings>();
            services.AddSingleton<Framework.Notifications.Sound.SoundScheme.SoundSchemeSettings>();
            services.AddSingleton<Framework.Appearance.IntelligentGeneration.AIColorScheme.AIColorSchemeSettings>();
            services.AddSingleton<Framework.Appearance.Animation.LoadingAnimation.LoadingAnimationSettings>();
            services.AddSingleton<Framework.Appearance.Animation.ThemeTransition.ThemeTransitionSettings>();
            services.AddSingleton<Framework.Appearance.Animation.UIResolution.UIResolutionSettings>();
            services.AddSingleton<Framework.Appearance.AutoTheme.SystemFollow.SystemFollowSettings>();
            services.AddSingleton<Framework.Appearance.AutoTheme.TimeBased.TimeBasedSettings>();
            services.AddSingleton<Framework.SystemSettings.Info.AppInfo.AppInfoSettings>();
            services.AddSingleton<Framework.SystemSettings.Info.DiagnosticInfo.DiagnosticInfoSettings>();
            services.AddSingleton<Framework.SystemSettings.Info.RuntimeEnv.RuntimeEnvSettings>();
            services.AddSingleton<Framework.SystemSettings.Info.SystemMonitor.SystemMonitorSettings>();
            services.AddSingleton<Framework.SystemSettings.Logging.LogFormat.LogFormatSettings>();
            services.AddSingleton<Framework.SystemSettings.Logging.LogLevel.LogLevelSettings>();
            services.AddSingleton<Framework.SystemSettings.Logging.LogOutput.LogOutputSettings>();
            services.AddSingleton<Framework.SystemSettings.Logging.LogRotation.LogRotationSettings>();
            services.AddSingleton<Framework.SystemSettings.Proxy.ProxyChain.ProxyChainSettings>();
            services.AddSingleton<Framework.SystemSettings.Proxy.ProxyRules.ProxyRulesSettings>();
            services.AddSingleton<Framework.SystemSettings.Proxy.ProxySetup.ProxySetupSettings>();
            services.AddSingleton<Framework.SystemSettings.Proxy.ProxyTest.ProxyTestSettings>();
            services.AddSingleton<Framework.UI.Windows.UnifiedWindowSettings>();

            services.AddTransient<Framework.Notifications.Sound.SoundScheme.SoundSchemeViewModel>();
            services.AddTransient<Framework.Notifications.Sound.VoiceBroadcast.VoiceBroadcastViewModel>();
            services.AddTransient<Framework.Notifications.Sound.VolumeAndDevice.VolumeAndDeviceViewModel>();
            services.AddTransient<Framework.Notifications.NotificationManagement.DoNotDisturb.DoNotDisturbViewModel>();
            services.AddTransient<Framework.Notifications.NotificationManagement.NotificationHistory.NotificationHistoryViewModel>();
            services.AddTransient<Framework.Notifications.SystemNotifications.NotificationStyle.NotificationStyleViewModel>();
            services.AddTransient<Framework.Notifications.SystemNotifications.NotificationTypes.NotificationTypesViewModel>();
            services.AddTransient<Framework.Notifications.SystemNotifications.SystemIntegration.SystemIntegrationViewModel>();

            services.AddTransient<Framework.SystemSettings.Proxy.ProxyRules.ProxyRulesViewModel>();
            services.AddTransient<Framework.SystemSettings.Proxy.ProxyTest.ProxyTestViewModel>();
            services.AddTransient<Framework.SystemSettings.Proxy.ProxySetup.ProxySetupViewModel>();
            services.AddTransient<Framework.SystemSettings.Proxy.ProxyChain.ProxyChainViewModel>();

            services.AddTransient<Framework.User.Account.AccountBinding.AccountBindingViewModel>();
            services.AddTransient<Framework.User.Account.PasswordSecurity.PasswordSecurityViewModel>();
            services.AddTransient<Framework.User.Preferences.Display.DisplayViewModel>();
            services.AddTransient<Framework.User.Profile.BasicInfo.BasicInfoViewModel>();
            services.AddTransient<Framework.User.Profile.Subscription.SubscriptionViewModel>();
            services.AddTransient<Framework.User.Security.PasswordProtection.AutoLock.AutoLockViewModel>();
            services.AddTransient<Framework.User.Security.PasswordProtection.PasswordLock.PasswordLockViewModel>();
            services.AddTransient<Framework.User.Preferences.Locale.LocaleViewModel>();

            services.AddTransient<Framework.UI.Workspace.Common.Controls.ProjectSpecPanelViewModel>();
            services.AddTransient<Framework.UI.Workspace.CenterPanel.ChapterEditor.PlanViewModel>();
            services.AddTransient<Framework.UI.Workspace.CenterPanel.Controls.VersionHistoryPanelViewModel>();
            services.AddTransient<Framework.UI.Workspace.RightPanel.Controls.ReferenceDropdownViewModel>();
            services.AddTransient<Framework.UI.Windows.UnifiedWindowViewModel>();
            services.AddTransient<Framework.UI.Workspace.Common.Controls.ProjectSelectorViewModel>();

            services.AddTransient<Framework.SystemSettings.DataCleanup.DataCleanupViewModel>();
            services.AddTransient<Framework.SystemSettings.Info.SystemInfo.SystemInfoViewModel>();
            services.AddTransient<Framework.SystemSettings.Logging.LogFormat.LogFormatViewModel>();
            services.AddTransient<Framework.SystemSettings.Logging.LogLevel.LogLevelViewModel>();
            services.AddTransient<Framework.SystemSettings.Logging.LogOutput.LogOutputViewModel>();

            services.AddTransient<Framework.User.Account.AccountDeletion.AccountDeletionViewModel>();
            services.AddTransient<Framework.User.Account.LoginHistory.LoginHistoryViewModel>();

            services.AddSingleton<Framework.UI.Workspace.Services.ReferenceParser>();
            services.AddSingleton<Framework.UI.Workspace.Services.ChapterGenerationBridge>();

            services.AddTransient<Framework.Appearance.Font.EditorFont.EditorFontViewModel>();
            services.AddTransient<Framework.Appearance.Font.UIFont.UIFontViewModel>();
            services.AddTransient<Framework.Appearance.ThemeManagement.ThemeDesign.ThemeDesignViewModel>();
            services.AddTransient<Framework.Appearance.ThemeManagement.ThemeImportExport.ThemeImportExportViewModel>();
            services.AddTransient<Framework.Appearance.AutoTheme.SystemFollow.SystemFollowViewModel>();
            services.AddTransient<Framework.Appearance.AutoTheme.TimeBased.TimeBasedViewModel>();
            services.AddTransient<Framework.Appearance.Animation.ThemeTransition.ThemeTransitionViewModel>();
            services.AddTransient<Framework.Appearance.Animation.LoadingAnimation.LoadingAnimationViewModel>();
            services.AddTransient<Framework.Appearance.Animation.UIResolution.UIResolutionViewModel>();
            services.AddTransient<Framework.Appearance.IntelligentGeneration.ImageColorPicker.ImageColorPickerViewModel>();
            services.AddTransient<Framework.Appearance.IntelligentGeneration.AIColorScheme.AIColorSchemeViewModel>();
            services.AddTransient<Framework.Appearance.IntelligentGeneration.GenerationHistory.GenerationHistoryViewModel>();

            services.AddTransient<Framework.UI.Workspace.RightPanel.Conversation.SKConversationViewModel>();

            services.AddSingleton<Services.Modules.ProjectData.Implementations.GeneratedContentService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ContextService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ValidationReportService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.UnifiedValidationService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IUnifiedValidationService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.UnifiedValidationService>());
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IValidationReportService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.ValidationReportService>());
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IContextService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.ContextService>());
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IGeneratedContentService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.GeneratedContentService>());
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.LedgerTrimService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ContentGenerationCallback>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.IndexService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IIndexService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.IndexService>());
            services.AddSingleton<Services.Modules.ProjectData.Implementations.RelationStrengthService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.ProgressiveSummaryService>();
            services.AddSingleton<Services.Modules.ProjectData.Implementations.GlobalSummaryService>();
            services.AddSingleton<Services.Modules.ProjectData.Interfaces.IGlobalSummaryService>(sp =>
                sp.GetRequiredService<Services.Modules.ProjectData.Implementations.GlobalSummaryService>());
            services.AddSingleton<Modules.Generate.Content.Services.ContentConfigService>();
            services.AddSingleton<Modules.Design.SmartParsing.BookAnalysis.Services.NovelCrawlerService>();
            services.AddSingleton<Framework.UI.Workspace.Services.ChapterVersionService>();
            services.AddSingleton<Framework.Common.Helpers.AI.PromptGenerationService>();
            services.AddSingleton<Framework.Common.Helpers.AI.IPromptGenerationService>(sp =>
                sp.GetRequiredService<Framework.Common.Helpers.AI.PromptGenerationService>());
            services.AddSingleton<Modules.Design.SmartParsing.BookAnalysis.Services.ConfigurableBookWebSearchProvider>();
            services.AddSingleton<Modules.Design.SmartParsing.BookAnalysis.Services.IBookWebSearchProvider>(sp =>
                sp.GetRequiredService<Modules.Design.SmartParsing.BookAnalysis.Services.ConfigurableBookWebSearchProvider>());
            services.AddSingleton<Modules.Design.SmartParsing.BookAnalysis.Services.EssenceChapterSelectionService>();

            services.AddTransient<Modules.AIAssistant.ModelIntegration.ModelManagement.ModelManagementViewModel>();
            services.AddTransient<Modules.AIAssistant.ModelIntegration.UsageStatistics.UsageStatisticsViewModel>();
            services.AddTransient<Modules.AIAssistant.PromptTools.PromptManagement.PromptManagementViewModel>();
            services.AddTransient<Modules.AIAssistant.PromptTools.VersionTesting.VersionTestingViewModel>();
            services.AddTransient<Modules.AIAssistant.MemoryManagement.MemoryManagementViewModel>();
            services.AddTransient<Modules.Validate.ValidationSummary.ValidationResult.ValidationResultViewModel>();
            services.AddTransient<Modules.Generate.GlobalSettings.Outline.OutlineViewModel>();
            services.AddTransient<Modules.Generate.Elements.VolumeDesign.VolumeDesignViewModel>();
            services.AddTransient<Modules.Generate.Elements.Chapter.ChapterViewModel>();
            services.AddTransient<Modules.Generate.Elements.Blueprint.BlueprintViewModel>();
            services.AddTransient<Modules.Generate.Content.ChapterPreview.ChapterPreviewViewModel>();
            services.AddTransient<Modules.Generate.Content.ContentViewModel>();
            services.AddTransient<Modules.Design.SmartParsing.BookAnalysis.BookAnalysisViewModel>();
            services.AddTransient<Modules.Design.Templates.CreativeMaterials.CreativeMaterialsViewModel>();
            services.AddTransient<Modules.Design.GlobalSettings.WorldRules.WorldRulesViewModel>();
            services.AddTransient<Modules.Design.Elements.CharacterRules.CharacterRulesViewModel>();
            services.AddTransient<Modules.Design.Elements.FactionRules.FactionRulesViewModel>();
            services.AddTransient<Modules.Design.Elements.LocationRules.LocationRulesViewModel>();
            services.AddTransient<Modules.Design.Elements.PlotRules.PlotRulesViewModel>();

            services.AddTransient<Modules.AIAssistant.ModelIntegration.ModelManagement.ModelManagementView>();
            services.AddTransient<Modules.AIAssistant.ModelIntegration.UsageStatistics.UsageStatisticsView>();
            services.AddTransient<Modules.AIAssistant.PromptTools.PromptManagement.PromptManagementView>();
            services.AddTransient<Modules.AIAssistant.PromptTools.VersionTesting.VersionTestingView>();
            services.AddTransient<Modules.AIAssistant.MemoryManagement.MemoryManagementView>();
            services.AddTransient<Modules.Validate.ValidationSummary.ValidationResult.ValidationResultView>();
            services.AddTransient<Modules.Validate.ValidationIntro.WorldviewIntro.WorldviewIntroView>();
            services.AddTransient<Modules.Validate.ValidationIntro.CharacterIntro.CharacterIntroView>();
            services.AddTransient<Modules.Validate.ValidationIntro.PlotIntro.PlotIntroView>();
            services.AddTransient<Modules.Validate.ValidationIntro.OutlineIntro.OutlineIntroView>();
            services.AddTransient<Modules.Validate.ValidationIntro.ChapterIntro.ChapterIntroView>();
            services.AddTransient<Modules.Validate.ValidationIntro.ContentIntro.ContentIntroView>();
            services.AddTransient<Modules.Generate.GlobalSettings.Outline.OutlineView>();
            services.AddTransient<Modules.Generate.Elements.VolumeDesign.VolumeDesignView>();
            services.AddTransient<Modules.Generate.Elements.Chapter.ChapterView>();
            services.AddTransient<Modules.Generate.Elements.Blueprint.BlueprintView>();
            services.AddTransient<Modules.Generate.Content.ChapterPreview.ChapterPreviewView>();
            services.AddTransient<Modules.Generate.Content.ContentView>();
            services.AddTransient<Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView>();
            services.AddTransient<Modules.Design.Templates.CreativeMaterials.CreativeMaterialsView>();
            services.AddTransient<Modules.Design.GlobalSettings.WorldRules.WorldRulesView>();
            services.AddTransient<Modules.Design.Elements.CharacterRules.CharacterRulesView>();
            services.AddTransient<Modules.Design.Elements.FactionRules.FactionRulesView>();
            services.AddTransient<Modules.Design.Elements.LocationRules.LocationRulesView>();
            services.AddTransient<Modules.Design.Elements.PlotRules.PlotRulesView>();

            _serviceProvider = services.BuildServiceProvider();
            if (!ServiceLocator.IsInitialized)
                ServiceLocator.Initialize(_serviceProvider);

            return _serviceProvider;
        }

        public static async Task InitializeServicesAsync(IServiceProvider serviceProvider)
        {
            serviceProvider.GetRequiredService<Framework.UI.Workspace.Services.ProjectManager>();

            serviceProvider.GetRequiredService<Services.Modules.ProjectData.Implementations.GuideManager>().RecoverPendingFlush();

            serviceProvider.GetRequiredService<Framework.UI.Workspace.Services.CurrentChapterPersistenceService>();

            var initTasks = new[]
            {
                Task.Run(() => serviceProvider.GetRequiredService<Services.Framework.AI.Core.AIService>()),
                serviceProvider.GetRequiredService<Modules.AIAssistant.PromptTools.PromptManagement.Services.PromptService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.AIAssistant.ModelIntegration.ModelManagement.Services.ModelService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Services.Modules.ProjectData.Interfaces.IWorkScopeService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.SmartParsing.BookAnalysis.Services.BookAnalysisService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.Templates.CreativeMaterials.Services.CreativeMaterialsService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.GlobalSettings.WorldRules.Services.WorldRulesService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.Elements.CharacterRules.Services.CharacterRulesService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.Elements.FactionRules.Services.FactionRulesService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.Elements.LocationRules.Services.LocationRulesService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Design.Elements.PlotRules.Services.PlotRulesService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Generate.GlobalSettings.Outline.Services.OutlineService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Generate.Elements.Chapter.Services.ChapterService>().InitializeAsync(),
                serviceProvider.GetRequiredService<Modules.Generate.Elements.Blueprint.Services.BlueprintService>().InitializeAsync(),
            };

            await Task.WhenAll(initTasks);
        }
    }
}
