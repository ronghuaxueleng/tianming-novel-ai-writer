using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.DataCleanup.Models;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Tracking;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Framework.SystemSettings.DataCleanup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DataCleanupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly AIService _aiService;
        private readonly SessionManager _sessionManager;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, string message, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[DataCleanup] {key}: {message} - {ex.Message}");
        }

        private bool _isLoading;
        private string _statusMessage = "";

        public DataCleanupViewModel(AIService aiService, SessionManager sessionManager)
        {
            _aiService = aiService;
            _sessionManager = sessionManager;
            Modules = new ObservableCollection<CleanupModule>();

            CleanupCommand = new RelayCommand(ExecuteCleanup, () => CanCleanup);
            SelectAllCommand = new RelayCommand(SelectAll);
            SelectNoneCommand = new RelayCommand(SelectNone);
            SelectModuleCommand = new RelayCommand(param => SelectModule(param as CleanupModule));
            RefreshCommand = new RelayCommand(LoadModules);

            LoadModules();
        }

        #region 属性

        public ObservableCollection<CleanupModule> Modules { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        public bool CanCleanup => Modules.Any(m => m.Items.Any(i => i.IsSelected));

        #endregion

        #region 命令

        public ICommand CleanupCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectModuleCommand { get; }
        public ICommand RefreshCommand { get; }

        #endregion

        #region 方法

        private async void LoadModules()
        {
            IsLoading = true;
            Modules.Clear();

            try
            {
                var whitelistModules = GetWhitelistModules();

                var modules = await System.Threading.Tasks.Task.Run(() => BuildModulesFromStorage(whitelistModules));

                foreach (var module in modules)
                    Modules.Add(module);

                StatusMessage = $"已加载 {Modules.Count} 个模块，共 {Modules.Sum(m => m.Items.Count)} 个清理项";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                TM.App.Log($"[DataCleanup] 加载模块失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private List<CleanupModule> GetWhitelistModules()
        {
            var modules = new List<CleanupModule>();
            var storageRoot = StoragePathHelper.GetProjectRoot();

            modules.Add(new CleanupModule
            {
                Id = "appearance",
                Name = "外观模块",
                Icon = "📊",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "生成历史记录",
                        FilePath = "Storage/Framework/Appearance/IntelligentGeneration/GenerationHistory/generation_history.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "network",
                Name = "网络模块",
                Icon = "🌐",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "代理测试历史",
                        FilePath = "Storage/Framework/Network/Proxy/test_history.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "notifications",
                Name = "通知模块",
                Icon = "🔔",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "通知历史记录",
                        FilePath = "Storage/Framework/Notifications/NotificationManagement/NotificationHistory/notification_history.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "systemsettings",
                Name = "系统设置",
                Icon = "⚙️",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "运行日志文件",
                        FilePath = "Storage/Logs",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "日志统计数据",
                        FilePath = "Storage/Framework/SystemSettings/Logging/LogLevel/statistics.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    },
                    new CleanupItem
                    {
                        Name = "输出统计数据",
                        FilePath = "Storage/Framework/SystemSettings/Logging/LogOutput/statistics.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "ui",
                Name = "UI模块",
                Icon = "🖥️",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "首页点击统计",
                        FilePath = "Storage/Framework/UI/Workspace/CenterPanel/ChapterEditor/homepage_click_counts.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "user",
                Name = "用户模块",
                Icon = "👤",
                Layer = "Framework",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "登录记录",
                        FilePath = "Storage/Framework/User/Account/LoginHistory/login_history.json",
                        RiskLevel = RiskLevel.Medium,
                        CleanupMethod = CleanupMethod.ClearContent
                    },
                    new CleanupItem
                    {
                        Name = "账户信息",
                        FilePath = "Storage/Framework/User/Account/Login/accounts.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后需要重新登录",
                        CleanupMethod = CleanupMethod.DeleteFile
                    },
                    new CleanupItem
                    {
                        Name = "记住的账户",
                        FilePath = "Storage/Framework/User/Account/Login/remembered.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后需要重新输入账号密码",
                        CleanupMethod = CleanupMethod.DeleteFile
                    },
                    new CleanupItem
                    {
                        Name = "认证令牌",
                        FilePath = "Storage/Framework/User/Services/auth_token.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后需要重新登录",
                        CleanupMethod = CleanupMethod.DeleteFile
                    },
                    new CleanupItem
                    {
                        Name = "订阅信息",
                        FilePath = "Storage/Framework/User/Services/subscription.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后需要重新同步订阅状态",
                        CleanupMethod = CleanupMethod.DeleteFile
                    },
                    new CleanupItem
                    {
                        Name = "用户资料",
                        FilePath = "Storage/Framework/User/Profile/BasicInfo",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后用户资料与头像将被重置",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "2FA密钥",
                        FilePath = "Storage/Framework/User/Account/PasswordSecurity/2fa_secret.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后需要重新设置两步验证",
                        CleanupMethod = CleanupMethod.DeleteFile
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "security",
                Name = "安全模块",
                Icon = "🛡️",
                Layer = "Framework",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "应用锁配置",
                        FilePath = "Storage/Framework/User/Security/PasswordProtection/app_lock_config.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后应用锁将被禁用",
                        CleanupMethod = CleanupMethod.DeleteFile
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "aiassistant",
                Name = "AI助手",
                Icon = "🤖",
                Layer = "Modules",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "用户自定义提示词模板",
                        FilePath = "Storage/Modules/AIAssistant/PromptTools/PromptManagement/templates",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "仅清除用户自定义模板，内置模板保留",
                        CleanupMethod = CleanupMethod.DeleteNonBuiltIn
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "aiservice",
                Name = "AI服务",
                Icon = "🧠",
                Layer = "Services",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "API调用统计",
                        FilePath = "Storage/Services/AI/Monitoring/api_statistics.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    },
                    new CleanupItem
                    {
                        Name = "模型配置（含 API Key）",
                        FilePath = "Storage/Services/AI/Configurations/user_configurations.json",
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除后所有模型配置和 API Key 将丢失",
                        CleanupMethod = CleanupMethod.DeleteFile
                    },
                    new CleanupItem
                    {
                        Name = "供应商模型库（含端点/API Key）",
                        FilePath = "Storage/Services/AI/Library/ProviderModels",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除所有供应商模型配置、端点和API Key",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "模型分类（保留LV1）",
                        FilePath = "Storage/Services/AI/Library/categories.json",
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "清除LV2及以下分类，保留官方/中转/个人模型一级分类",
                        CleanupMethod = CleanupMethod.ClearModelCategoriesKeepLevel1
                    },
                    new CleanupItem
                    {
                        Name = "供应商列表",
                        FilePath = "Storage/Services/AI/Library/providers.json",
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "清除供应商列表配置",
                        CleanupMethod = CleanupMethod.ClearContent
                    },
                    new CleanupItem
                    {
                        Name = "参数配置模板",
                        FilePath = "Storage/Services/AI/Library/parameter-profiles.json",
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "projects",
                Name = "项目数据",
                Icon = "📁",
                Layer = "Projects",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "文档管理区（卷+章节）",
                        FilePath = "Storage/Projects/Generated",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除所有项目的卷（LV2）和章节内容",
                        CleanupMethod = CleanupMethod.ClearProjectVolumesAndChapters
                    },
                    new CleanupItem
                    {
                        Name = "项目引导数据",
                        FilePath = "Storage/Projects/Config/guides",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除项目的蓝图、目录、角色状态等引导数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "打包配置数据",
                        FilePath = "Storage/Projects/Config",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除所有项目的打包配置数据",
                        CleanupMethod = CleanupMethod.ClearProjectConfigData
                    },
                    new CleanupItem
                    {
                        Name = "打包历史记录",
                        FilePath = "Storage/Projects/History",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "清除所有项目的打包历史版本",
                        CleanupMethod = CleanupMethod.ClearProjectHistory
                    },
                    new CleanupItem
                    {
                        Name = "校验报告",
                        FilePath = "Storage/Projects/Validation/reports",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        WarningMessage = "清除所有项目的校验报告",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "会话数据",
                        FilePath = "Storage/Projects/Sessions",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        WarningMessage = "清除所有项目的会话消息和记忆数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "版本注册表",
                        FilePath = "Storage/Projects/VersionRegistry",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "design",
                Name = "设计模块",
                Icon = "🎨",
                Layer = "Modules",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "智能拆书（书籍分析）",
                        FilePath = "Storage/Modules/Design/SmartParsing",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除书籍分析数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "创作模板（创作素材）",
                        FilePath = "Storage/Modules/Design/Templates",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除创作素材数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "全局设定",
                        FilePath = "Storage/Modules/Design/GlobalSettings",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除世界观规则数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "设计元素",
                        FilePath = "Storage/Modules/Design/Elements",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除角色、势力、剧情规则数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "generate",
                Name = "生成模块",
                Icon = "⚙️",
                Layer = "Modules",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "全书设定",
                        FilePath = "Storage/Modules/Generate/GlobalSettings",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除故事框架、卷级大纲、结局设计数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "创作元素",
                        FilePath = "Storage/Modules/Generate/Elements",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除分卷设计、章节划分、蓝图设计等数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "正文配置",
                        FilePath = "Storage/Modules/Generate/Content/config.json",
                        RiskLevel = RiskLevel.Low,
                        WarningMessage = "清除正文配置设定",
                        CleanupMethod = CleanupMethod.ClearContent
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "validate",
                Name = "校验模块",
                Icon = "✅",
                Layer = "Modules",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "校验汇总数据",
                        FilePath = "Storage/Modules/Validate/ValidationSummary/data",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除所有卷校验汇总数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "ui_state",
                Name = "UI状态",
                Icon = "🖥️",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "工作区面板布局",
                        FilePath = "Storage/Framework/UI/Workspace",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "工作区配置",
                        FilePath = "Storage/Framework/UI/Workspaces",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "user_preferences",
                Name = "用户偏好",
                Icon = "⚙️",
                Layer = "Framework",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "用户偏好设置",
                        FilePath = "Storage/Framework/User/Preferences",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "清除用户个性化偏好设置",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "framework_settings",
                Name = "框架设置",
                Icon = "🔧",
                Layer = "Services",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "框架级设置",
                        FilePath = "Storage/Services/Settings",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Medium,
                        WarningMessage = "清除主题偏好、上下文扩展等框架设置",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                }
            });

            modules.Add(new CleanupModule
            {
                Id = "vector_index",
                Name = "对话向量索引",
                Icon = "🔍",
                Layer = "Services",
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "对话 RAG 向量索引",
                        FilePath = "Storage/Services/AI/VectorIndex",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        WarningMessage = "清除对话向量索引（下次对话时自动重建）",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    }
                }
            });

            return modules;
        }

        private List<CleanupModule> BuildModulesFromStorage(List<CleanupModule> overrideModules)
        {
            var result = new List<CleanupModule>();
            var storageRoot = StoragePathHelper.GetStorageRoot();

            var chineseNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Animation"] = "动画效果",
                ["Font"] = "字体配置",
                ["IntelligentGeneration"] = "智能生成历史",
                ["ThemeManagement"] = "主题管理",
                ["standard_form_options.json"] = "标准表单选项",
                ["Proxy"] = "代理配置",
                ["NotificationManagement"] = "通知管理",
                ["Sound"] = "声音配置",
                ["SystemNotifications"] = "系统通知",
                ["PasswordProtection"] = "密码保护",
                ["Info"] = "系统信息",
                ["Logging"] = "日志配置",
                ["Windows"] = "窗口状态",
                ["Workspace"] = "工作区",
                ["Workspaces"] = "工作区配置",
                ["Account"] = "账户信息",
                ["Preferences"] = "用户偏好",
                ["Profile"] = "用户资料",
                ["Security"] = "安全设置",
                ["Services"] = "用户服务",
                ["ModelManagement"] = "模型管理",
                ["PromptTools"] = "提示词工具",
                ["Elements"] = "设计元素",
                ["GlobalSettings"] = "全局设定",
                ["SmartParsing"] = "智能拆书",
                ["Templates"] = "创作模板",
                ["Content"] = "正文配置",
                ["ValidationSummary"] = "校验汇总",
                ["Capabilities"] = "AI能力配置",
                ["Configurations"] = "模型配置",
                ["Conversations"] = "会话历史",
                ["Library"] = "模型库",
                ["Sessions"] = "会话消息",
                ["context_expansion_config.json"] = "上下文扩展配置",
                ["version_registry.json"] = "版本注册表"
            };

            var moduleNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Appearance"] = "外观模块",
                ["Common"] = "通用模块",
                ["Network"] = "网络模块",
                ["Notifications"] = "通知模块",
                ["Security"] = "安全模块",
                ["SystemSettings"] = "系统设置",
                ["UI"] = "UI模块",
                ["User"] = "用户模块",
                ["AIAssistant"] = "AI助手",
                ["Design"] = "设计模块",
                ["Generate"] = "生成模块",
                ["Validate"] = "校验模块",
                ["AI"] = "AI服务",
                ["Settings"] = "设置服务",
                ["VersionTracking"] = "版本追踪"
            };

            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Storage/Framework/Appearance/Animation",
                "Storage/Framework/Appearance/Font",
                "Storage/Framework/Common/IpLocation",
                "Storage/Framework/UI/Windows",
                "Storage/Services/AI/Capabilities",
                "Storage/Services/Framework/AI",
            };

            var whitelistLookup = new Dictionary<string, (CleanupModule Module, CleanupItem Item)>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in overrideModules)
            {
                foreach (var item in module.Items)
                {
                    var normalizedPath = NormalizePath(item.FilePath);
                    whitelistLookup[normalizedPath] = (module, item);
                }
            }

            var layers = new[] { "Framework", "Modules", "Services" };
            foreach (var layer in layers)
            {
                var layerPath = Path.Combine(storageRoot, layer);
                if (!Directory.Exists(layerPath)) continue;

                foreach (var level1Dir in Directory.GetDirectories(layerPath))
                {
                    var level1Name = Path.GetFileName(level1Dir);
                    var moduleItems = new List<CleanupItem>();

                    foreach (var level2Dir in Directory.GetDirectories(level1Dir))
                    {
                        var level2Name = Path.GetFileName(level2Dir);
                        var relativePath = $"Storage/{layer}/{level1Name}/{level2Name}";
                        var normalizedPath = NormalizePath(relativePath);

                        if (!DirectoryHasRealData(level2Dir)) continue;

                        if (protectedPaths.Contains(normalizedPath)) continue;

                        CleanupItem item;
                        if (whitelistLookup.TryGetValue(normalizedPath, out var match))
                        {
                            item = match.Item;
                        }
                        else
                        {
                            var displayName = chineseNameMap.TryGetValue(level2Name, out var cn) ? cn : level2Name;
                            item = new CleanupItem
                            {
                                Name = displayName,
                                FilePath = relativePath,
                                IsDirectory = true,
                                RiskLevel = RiskLevel.Medium,
                                CleanupMethod = CleanupMethod.ClearDirectory
                            };
                        }

                        moduleItems.Add(item);
                    }

                    foreach (var file in Directory.GetFiles(level1Dir))
                    {
                        var fileName = Path.GetFileName(file);
                        var relativePath = $"Storage/{layer}/{level1Name}/{fileName}";
                        var normalizedPath = NormalizePath(relativePath);

                        if (IsProtectedBuiltInFile(file)) continue;

                        if (!FileHasRealData(file)) continue;

                        CleanupItem item;
                        if (whitelistLookup.TryGetValue(normalizedPath, out var match))
                        {
                            item = match.Item;
                        }
                        else
                        {
                            var displayName = chineseNameMap.TryGetValue(fileName, out var cn) ? cn : fileName;
                            item = new CleanupItem
                            {
                                Name = displayName,
                                FilePath = relativePath,
                                IsDirectory = false,
                                RiskLevel = RiskLevel.Medium,
                                CleanupMethod = CleanupMethod.ClearContent
                            };
                        }

                        moduleItems.Add(item);
                    }

                    if (moduleItems.Count == 0) continue;

                    var whitelistModule = overrideModules.FirstOrDefault(m =>
                        m.Layer == layer && m.Items.Any(i => i.FilePath.Contains($"/{level1Name}/")));

                    var moduleDisplayName = whitelistModule?.Name 
                        ?? (moduleNameMap.TryGetValue(level1Name, out var mnCn) ? mnCn : level1Name);
                    var module = new CleanupModule
                    {
                        Id = $"{layer.ToLower()}_{level1Name.ToLower()}",
                        Name = moduleDisplayName,
                        Icon = whitelistModule?.Icon ?? GetDefaultIcon(layer),
                        Layer = layer,
                        IsDangerous = whitelistModule?.IsDangerous ?? false,
                        Items = moduleItems
                    };

                    result.Add(module);
                }
            }

            var projectsModule = BuildProjectsModule(storageRoot, overrideModules);
            if (projectsModule != null && projectsModule.Items.Count > 0)
            {
                result.Add(projectsModule);
            }

            return result;
        }

        private CleanupModule? BuildProjectsModule(string storageRoot, List<CleanupModule> overrideModules)
        {
            var projectsDir = Path.Combine(storageRoot, "Projects");
            if (!Directory.Exists(projectsDir)) return null;

            var projectDirs = Directory.GetDirectories(projectsDir);
            if (projectDirs.Length == 0) return null;

            var whitelistModule = overrideModules.FirstOrDefault(m => m.Layer == "Projects");
            if (whitelistModule == null) return null;

            var validItems = new List<CleanupItem>();

            foreach (var item in whitelistModule.Items)
            {
                var hasData = ProjectsHaveData(projectDirs, item.FilePath);
                if (hasData)
                {
                    validItems.Add(item);
                }
            }

            if (validItems.Count == 0) return null;

            return new CleanupModule
            {
                Id = whitelistModule.Id,
                Name = whitelistModule.Name,
                Icon = whitelistModule.Icon,
                Layer = "Projects",
                IsDangerous = whitelistModule.IsDangerous,
                Items = validItems
            };
        }

        private bool ProjectsHaveData(string[] projectDirs, string templatePath)
        {
            var subPath = templatePath.Replace("Storage/Projects/", "").Replace("Storage/Projects", "");
            if (string.IsNullOrEmpty(subPath)) subPath = "";

            foreach (var projectDir in projectDirs)
            {
                var targetPath = string.IsNullOrEmpty(subPath) 
                    ? projectDir 
                    : Path.Combine(projectDir, subPath.TrimStart('/'));

                if (Directory.Exists(targetPath))
                {
                    if (DirectoryHasRealData(targetPath)) return true;
                }
                else if (File.Exists(targetPath))
                {
                    if (FileHasRealData(targetPath)) return true;
                }
            }

            return false;
        }

        private static bool DirectoryHasRealData(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return false;

            foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                if (FileHasRealData(file)) return true;
            }

            return false;
        }

        private static bool FileHasRealData(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0) return false;

            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var content = File.ReadAllText(filePath).Trim();
                    if (content == "[]" || content == "{}" || content == "null" || string.IsNullOrWhiteSpace(content))
                        return false;
                }
                catch
                {
                }
            }

            return true;
        }

        private static string GetDefaultIcon(string layer)
        {
            return layer switch
            {
                "Framework" => "⚙️",
                "Modules" => "📦",
                "Services" => "🔧",
                "Projects" => "📁",
                _ => "📄"
            };
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            while (normalized.Contains("//", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            }

            return normalized.Trim();
        }

        private static bool DirectoryHasAnyFiles(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                return false;

            return Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).Any();
        }

        private void SelectAll()
        {
            foreach (var module in Modules)
            {
                module.IsSelected = true;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private void SelectNone()
        {
            foreach (var module in Modules)
            {
                module.IsSelected = false;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private void SelectModule(CleanupModule? module)
        {
            if (module == null) return;

            foreach (var item in module.Items)
            {
                item.IsSelected = true;
            }
            OnPropertyChanged(nameof(CanCleanup));
        }

        private async void ExecuteCleanup()
        {
            var selectedItems = Modules
                .SelectMany(m => m.Items)
                .Where(i => i.IsSelected)
                .ToList();

            if (!selectedItems.Any())
            {
                GlobalToast.Warning("提示", "请先选择要清理的数据");
                return;
            }

            var hasHighRisk = selectedItems.Any(i => i.RiskLevel == RiskLevel.High);

            var itemList = string.Join("\n", selectedItems.Select(i => $"• {i.Name}"));
            var confirmMessage = $"将清理以下 {selectedItems.Count} 项数据：\n\n{itemList}";

            if (!StandardDialog.ShowConfirm(confirmMessage, "确认清理"))
                return;

            if (hasHighRisk)
            {
                var highRiskItems = selectedItems.Where(i => i.RiskLevel == RiskLevel.High).ToList();
                var warningList = string.Join("\n", highRiskItems.Select(i => $"⚠️ {i.Name}: {i.WarningMessage}"));
                var warningMessage = $"⚠️ 警告：以下高危数据将被清除！\n\n{warningList}\n\n此操作不可恢复，确定继续？";

                if (!StandardDialog.ShowConfirm(warningMessage, "⚠️ 高危操作确认"))
                    return;
            }

            var storageRoot = StoragePathHelper.GetProjectRoot();
            var (successCount, failCount) = await System.Threading.Tasks.Task.Run(() =>
            {
                int success = 0, fail = 0;
                var lockObj = new object();

                System.Threading.Tasks.Parallel.ForEach(selectedItems, item =>
                {
                    try
                    {
                        var fullPath = Path.Combine(storageRoot, item.FilePath);

                        switch (item.CleanupMethod)
                        {
                            case CleanupMethod.ClearContent:
                                ClearFileContent(fullPath);
                                break;
                            case CleanupMethod.DeleteFile:
                                DeleteFileIfExists(fullPath);
                                break;
                            case CleanupMethod.ClearDirectory:
                                ClearDirectoryFiles(fullPath, item.FilePath);
                                break;
                            case CleanupMethod.DeleteNonBuiltIn:
                                DeleteNonBuiltInTemplates(fullPath);
                                break;
                            case CleanupMethod.ClearProjectCategories:
                                ClearAllProjectCategories();
                                break;
                            case CleanupMethod.ClearModelCategoriesKeepLevel1:
                                ClearModelCategoriesKeepLevel1(fullPath);
                                break;
                            case CleanupMethod.ClearProjectVolumesAndChapters:
                                ClearProjectVolumesAndChapters();
                                break;
                            case CleanupMethod.ClearProjectConfigData:
                                ClearProjectConfigData();
                                break;
                            case CleanupMethod.ClearProjectHistory:
                                ClearProjectHistory();
                                break;
                        }

                        lock (lockObj) { success++; }
                        TM.App.Log($"[DataCleanup] 清理成功: {item.Name}");
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj) { fail++; }
                        TM.App.Log($"[DataCleanup] 清理失败: {item.Name} - {ex.Message}");
                    }
                });

                return (success, fail);
            });

            if (failCount == 0)
                GlobalToast.Success("清理完成", $"成功清理 {successCount} 项数据");
            else
                GlobalToast.Warning("清理完成", $"成功 {successCount} 项，失败 {failCount} 项");

            RefreshServicesAfterCleanup(selectedItems);
            SelectNone();
            StatusMessage = $"清理完成：成功 {successCount} 项，失败 {failCount} 项";
        }

        private void ClearFileContent(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var content = File.ReadAllText(filePath);
            if (content.TrimStart().StartsWith("["))
            {
                File.WriteAllText(filePath, "[]");
            }
            else
            {
                File.WriteAllText(filePath, "{}");
            }
        }

        private void DeleteFileIfExists(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void ClearDirectoryFiles(string dirPath, string relativePath)
        {
            if (relativePath.StartsWith("Storage/Projects"))
            {
                var storageRoot = StoragePathHelper.GetProjectRoot();
                var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

                if (!Directory.Exists(projectsDir)) return;

                var deletedCount = 0;
                var failedCount = 0;

                foreach (var projectDir in Directory.GetDirectories(projectsDir))
                {
                    if (relativePath.Contains("Generated/chapters"))
                    {
                        var chaptersDir = Path.Combine(projectDir, "Generated", "chapters");
                        if (Directory.Exists(chaptersDir))
                        {
                            foreach (var file in Directory.GetFiles(chaptersDir, "*.*", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Config/guides"))
                    {
                        var guidesDir = Path.Combine(projectDir, "Config", "guides");
                        if (Directory.Exists(guidesDir))
                        {
                            foreach (var file in Directory.GetFiles(guidesDir, "*.json", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Validation/reports"))
                    {
                        var reportsDir = Path.Combine(projectDir, "Validation", "reports");
                        if (Directory.Exists(reportsDir))
                        {
                            foreach (var file in Directory.GetFiles(reportsDir, "*.json", SearchOption.AllDirectories))
                            {
                                try
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                                }
                            }
                        }
                    }
                    else if (relativePath.Contains("Sessions"))
                    {
                        var sessionsDir = Path.Combine(projectDir, "Sessions");
                        if (Directory.Exists(sessionsDir))
                        {
                            var indexFile = Path.Combine(sessionsDir, "_index.json");
                            if (File.Exists(indexFile))
                            {
                                try { File.WriteAllText(indexFile, "{}"); }
                                catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}"); }
                            }
                            foreach (var file in Directory.GetFiles(sessionsDir, "*.json")
                                .Where(f => !f.EndsWith("_index.json", StringComparison.OrdinalIgnoreCase)))
                            {
                                try { File.Delete(file); deletedCount++; }
                                catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}"); }
                            }
                        }
                    }
                    else if (relativePath.Contains("VersionRegistry"))
                    {
                        var registryFile = Path.Combine(projectDir, "version_registry.json");
                        if (File.Exists(registryFile))
                        {
                            try { File.Delete(registryFile); deletedCount++; }
                            catch (Exception ex) { failedCount++; TM.App.Log($"[DataCleanup] 删除文件失败: {registryFile} - {ex.Message}"); }
                        }
                    }
                }

                TM.App.Log($"[DataCleanup] Projects层目录清理完成: {relativePath}，成功 {deletedCount}，失败 {failedCount}");
                return;
            }

            if (!Directory.Exists(dirPath))
            {
                TM.App.Log($"[DataCleanup] 目录不存在: {dirPath}");
                return;
            }

            if (relativePath.Contains("Profile/BasicInfo"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                foreach (var file in Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                TM.App.Log($"[DataCleanup] 用户资料目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
                return;
            }

            else if (relativePath.Contains("Conversations"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                var indexFile = Path.Combine(dirPath, "conversation_index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        File.WriteAllText(indexFile, "{\"Sessions\":[]}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}");
                    }
                }
                var sessionsDir = Path.Combine(dirPath, "sessions");
                if (Directory.Exists(sessionsDir))
                {
                    foreach (var file in Directory.GetFiles(sessionsDir, "*.json"))
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                        }
                    }
                }

                TM.App.Log($"[DataCleanup] Conversations目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
            }
            else if (relativePath.Contains("Sessions") && !relativePath.Contains("Conversations"))
            {
                var deletedCount = 0;
                var failedCount = 0;

                var indexFile = Path.Combine(dirPath, "_index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        File.WriteAllText(indexFile, "{}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 写入索引失败: {indexFile} - {ex.Message}");
                    }
                }
                foreach (var file in Directory.GetFiles(dirPath, "*.messages.json"))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                TM.App.Log($"[DataCleanup] Sessions目录清理完成: {dirPath}，成功 {deletedCount}，失败 {failedCount}");
            }
            else
            {
                var deletedCount = 0;
                var failedCount = 0;
                var skippedCount = 0;

                foreach (var file in Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories))
                {
                    if (IsProtectedBuiltInFile(file))
                    {
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                if (failedCount > 0)
                    TM.App.Log($"[DataCleanup] 目录清理完成: {dirPath}，成功 {deletedCount}" + (skippedCount > 0 ? $"，跳过内置 {skippedCount}" : "") + $"，失败 {failedCount}");
                else
                    System.Diagnostics.Debug.WriteLine($"[DataCleanup] 目录清理完成: {dirPath}，成功 {deletedCount}" + (skippedCount > 0 ? $"，跳过内置 {skippedCount}" : ""));
            }
        }

        private static bool IsProtectedBuiltInFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            if (string.Equals(fileName, "built_in_categories.json", StringComparison.OrdinalIgnoreCase))
                return true;

            var normalizedPath = filePath.Replace('\\', '/');
            if (normalizedPath.Contains("/built_in_templates/", StringComparison.OrdinalIgnoreCase))
                return true;

            var protectedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "standard_form_options.json",
                "provider-logos.json",
                "model-capabilities.json",
                "ip2region_v4.xdb",
                "model.onnx",
                "vocab.txt",
                "doudi.png",
            };

            if (protectedFileNames.Contains(fileName))
                return true;

            return false;
        }

        private void DeleteNonBuiltInTemplates(string templatesDir)
        {
            if (!Directory.Exists(templatesDir)) return;

            foreach (var file in Directory.GetFiles(templatesDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var jsonDoc = JsonDocument.Parse(content);

                    if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var templates = new List<JsonElement>();
                        foreach (var item in jsonDoc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("IsBuiltIn", out var isBuiltIn) && isBuiltIn.GetBoolean())
                            {
                                templates.Add(item);
                            }
                        }

                        var options = JsonHelper.Default;
                        var newContent = JsonSerializer.Serialize(templates, options);
                        var tmpDc1 = file + ".tmp";
                        File.WriteAllText(tmpDc1, newContent);
                        File.Move(tmpDc1, file, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogOnce(nameof(DeleteNonBuiltInTemplates), file, ex);
                }
            }
        }

        private void ClearProjectVolumesAndChapters()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir))
            {
                TM.App.Log($"[DataCleanup] 项目目录不存在: {projectsDir}");
                return;
            }

            var clearedVolumes = 0;
            var clearedChapters = 0;
            var failedCount = 0;

            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var categoriesFile = Path.Combine(projectDir, "Generated", "categories.json");
                if (File.Exists(categoriesFile))
                {
                    try
                    {
                        File.WriteAllText(categoriesFile, "[]");
                        clearedVolumes++;
                        TM.App.Log($"[DataCleanup] 已清空卷数据: {categoriesFile}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 清空卷数据失败: {categoriesFile} - {ex.Message}");
                    }
                }

                var chaptersDir = Path.Combine(projectDir, "Generated", "chapters");
                if (Directory.Exists(chaptersDir))
                {
                    var files = Directory.GetFiles(chaptersDir, "*.md", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            clearedChapters++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除章节失败: {file} - {ex.Message}");
                        }
                    }
                    foreach (var ext in new[] { "*.bak", "*.staging" })
                    {
                        foreach (var file in Directory.GetFiles(chaptersDir, ext, SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); }
                            catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理{ext}失败: {ex.Message}"); }
                        }
                    }
                    TM.App.Log($"[DataCleanup] 已删除 {files.Length} 个章节文件: {chaptersDir}");
                }
            }

            TM.App.Log($"[DataCleanup] 已清除 {clearedVolumes} 个项目的卷数据，{clearedChapters} 个章节文件，失败 {failedCount}");

            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var guidesDir = Path.Combine(projectDir, "Config", "guides");
                if (!Directory.Exists(guidesDir)) continue;

                var trackingPrefixes = new[]
                {
                    "character_state_guide",
                    "location_state_guide",
                    "faction_state_guide",
                    "item_state_guide",
                    "timeline_guide",
                    "conflict_progress_guide",
                    "foreshadowing_status_guide",
                    "chapter_summary"
                };

                foreach (var file in Directory.GetFiles(guidesDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var fn = Path.GetFileName(file);
                    if (trackingPrefixes.Any(p => fn.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        try { File.Delete(file); TM.App.Log($"[DataCleanup] 联动清理追踪Guide: {fn}"); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理追踪Guide失败: {fn} - {ex.Message}"); }
                    }
                }

                var archivesDir = Path.Combine(guidesDir, "fact_archives");
                if (Directory.Exists(archivesDir))
                {
                    foreach (var file in Directory.GetFiles(archivesDir, "*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理卷末存档失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理卷末事实存档: {archivesDir}");
                }

                var milestonesDir = Path.Combine(guidesDir, "milestones");
                if (Directory.Exists(milestonesDir))
                {
                    foreach (var file in Directory.GetFiles(milestonesDir, "*.*"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理里程碑失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理里程碑文件: {milestonesDir}");
                }

                var plotPointsDir = Path.Combine(guidesDir, "plot_points");
                if (Directory.Exists(plotPointsDir))
                {
                    foreach (var file in Directory.GetFiles(plotPointsDir, "*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理情节点分片失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理情节点分片: {plotPointsDir}");
                }

                var summariesDir = Path.Combine(guidesDir, "summaries");
                if (Directory.Exists(summariesDir))
                {
                    foreach (var file in Directory.GetFiles(summariesDir, "vol*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理摘要分片失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理章节摘要分片: {summariesDir}");
                }

                var kwIndexFile = Path.Combine(guidesDir, "keyword_index.json");
                if (File.Exists(kwIndexFile))
                {
                    try { File.Delete(kwIndexFile); TM.App.Log($"[DataCleanup] 已清理关键词索引: keyword_index.json"); }
                    catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理关键词索引失败: {ex.Message}"); }
                }
            }
            TM.App.Log("[DataCleanup] 联动追踪Guide清理完成");

            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var vectorIndexDir = Path.Combine(projectDir, "VectorIndex");
                if (Directory.Exists(vectorIndexDir))
                {
                    foreach (var file in Directory.GetFiles(vectorIndexDir, "*.json"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex) { TM.App.Log($"[DataCleanup] 清理向量索引失败: {ex.Message}"); }
                    }
                    TM.App.Log($"[DataCleanup] 已清理向量索引: {vectorIndexDir}");
                }
            }
            TM.App.Log("[DataCleanup] 联动向量索引清理完成");

            try
            {
                ServiceLocator.Get<GuideManager>().DiscardDirtyAndEvict();
                ServiceLocator.Get<GuideContextService>().ClearCache();
                ServiceLocator.Get<IGlobalSummaryService>().InvalidateCache();
                ServiceLocator.Get<RelationStrengthService>().InvalidateCache();
                ServiceLocator.Get<VolumeFactArchiveStore>().InvalidateCache();
                TM.App.Log("[DataCleanup] 内存缓存已失效");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 缓存失效调用失败（不影响清理结果）: {ex.Message}");
            }
        }

        private void ClearProjectConfigData()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir)) return;

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var configDir = Path.Combine(projectDir, "Config");
                if (Directory.Exists(configDir))
                {
                    foreach (var file in Directory.GetFiles(configDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            File.Delete(file);
                            clearedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除文件失败: {file} - {ex.Message}");
                        }
                    }
                    var subDirs = new[] { "Design", "Generate" };
                    foreach (var subDir in subDirs)
                    {
                        var subPath = Path.Combine(configDir, subDir);
                        if (Directory.Exists(subPath))
                        {
                            try
                            {
                                Directory.Delete(subPath, true);
                                TM.App.Log($"[DataCleanup] 已删除打包目录: {subPath}");
                            }
                            catch (Exception ex)
                            {
                                failedCount++;
                                TM.App.Log($"[DataCleanup] 删除目录失败: {subPath} - {ex.Message}");
                            }
                        }
                    }
                }
            }
            TM.App.Log($"[DataCleanup] 已清除打包配置数据: 成功 {clearedCount}，失败 {failedCount}");
        }

        private void ClearProjectHistory()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir)) return;

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var historyDir = Path.Combine(projectDir, "History");
                if (Directory.Exists(historyDir))
                {
                    foreach (var versionDir in Directory.GetDirectories(historyDir))
                    {
                        try
                        {
                            Directory.Delete(versionDir, true);
                            clearedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            TM.App.Log($"[DataCleanup] 删除目录失败: {versionDir} - {ex.Message}");
                        }
                    }
                    TM.App.Log($"[DataCleanup] 已删除打包历史: {historyDir}");
                }
            }
            TM.App.Log($"[DataCleanup] 已清除历史版本: 成功 {clearedCount}，失败 {failedCount}");
        }

        private void ClearAllProjectCategories()
        {
            var storageRoot = StoragePathHelper.GetProjectRoot();
            var projectsDir = Path.Combine(storageRoot, "Storage", "Projects");

            if (!Directory.Exists(projectsDir))
            {
                TM.App.Log($"[DataCleanup] 项目目录不存在: {projectsDir}");
                return;
            }

            var clearedCount = 0;
            var failedCount = 0;
            foreach (var projectDir in Directory.GetDirectories(projectsDir))
            {
                var categoriesFile = Path.Combine(projectDir, "Generated", "categories.json");
                if (File.Exists(categoriesFile))
                {
                    try
                    {
                        File.WriteAllText(categoriesFile, "[]");
                        clearedCount++;
                        TM.App.Log($"[DataCleanup] 已清空分类文件: {categoriesFile}");
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        TM.App.Log($"[DataCleanup] 清空分类文件失败: {categoriesFile} - {ex.Message}");
                    }
                }
            }
            TM.App.Log($"[DataCleanup] 已清空项目分类数据: 成功 {clearedCount}，失败 {failedCount}");
        }

        private void ClearModelCategoriesKeepLevel1(string filePath)
        {
            if (!File.Exists(filePath))
            {
                TM.App.Log($"[DataCleanup] 文件不存在: {filePath}");
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                var jsonDoc = JsonDocument.Parse(content);

                if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    TM.App.Log($"[DataCleanup] categories.json 不是数组格式");
                    return;
                }

                var level1Categories = new List<Dictionary<string, object>>();
                foreach (var item in jsonDoc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("Level", out var levelProp) && levelProp.GetInt32() == 1)
                    {
                        var category = new Dictionary<string, object>();
                        foreach (var prop in item.EnumerateObject())
                        {
                            category[prop.Name] = GetJsonValue(prop.Value) ?? string.Empty;
                        }
                        level1Categories.Add(category);
                    }
                }

                var options = JsonHelper.Default;
                var newContent = JsonSerializer.Serialize(level1Categories, options);
                var tmpDc2 = filePath + ".tmp";
                File.WriteAllText(tmpDc2, newContent);
                File.Move(tmpDc2, filePath, overwrite: true);

                TM.App.Log($"[DataCleanup] 已清除模型分类，保留 {level1Categories.Count} 个LV1分类");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 清除模型分类失败: {ex.Message}");
                throw;
            }
        }

        private object? GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        private void RefreshServicesAfterCleanup(List<CleanupItem> cleanedItems)
        {
            try
            {
                var refreshedServices = new List<string>();

                var aiRelated = cleanedItems.Any(i => 
                    i.FilePath.Contains("Services/AI/Library") ||
                    i.FilePath.Contains("Services/AI/Configurations"));

                if (aiRelated)
                {
                    _aiService.ReloadLibrary();
                    refreshedServices.Add("AIService");
                }

                var sessionRelated = cleanedItems.Any(i => 
                    i.FilePath.Contains("Projects/Sessions"));

                if (sessionRelated)
                {
                    _sessionManager.ReloadIndex();
                    refreshedServices.Add("SessionManager");
                }

                if (refreshedServices.Count > 0)
                {
                    TM.App.Log($"[DataCleanup] 已刷新服务: {string.Join(", ", refreshedServices)}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataCleanup] 刷新服务失败: {ex.Message}");
            }
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
