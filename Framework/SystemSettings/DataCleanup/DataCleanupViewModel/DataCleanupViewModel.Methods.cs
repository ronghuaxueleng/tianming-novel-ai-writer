using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.DataCleanup.Models;

namespace TM.Framework.SystemSettings.DataCleanup
{
    public partial class DataCleanupViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        #region 方法

        private async Task LoadModules()
        {
            IsLoading = true;
            try
            {
                var whitelistModules = GetWhitelistModules();

                var modules = await System.Threading.Tasks.Task.Run(() => BuildModulesFromStorageAsync(whitelistModules)).ConfigureAwait(true);

                Modules.ReplaceAll(modules);

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
                Icon = "Icon.Chart",
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
                Icon = "Icon.Globe",
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
                Icon = "Icon.Bell",
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
                Icon = "Icon.Settings",
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
                Icon = "Icon.Monitor",
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
                Icon = "Icon.User",
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
                Icon = "Icon.Lock",
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
                Icon = "Icon.Sparkle",
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
                Icon = "Icon.Brain",
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
                Icon = "Icon.Folder",
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
                Icon = "Icon.Palette",
                Layer = "Modules",
                IsDangerous = true,
                Items = new List<CleanupItem>
                {
                    new CleanupItem
                    {
                        Name = "智能拆书（书籍分析）",
                        FilePath = "Storage/Modules/Design/SmartParsing/BookAnalysis",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.High,
                        WarningMessage = "清除书籍分析数据",
                        CleanupMethod = CleanupMethod.ClearDirectory
                    },
                    new CleanupItem
                    {
                        Name = "提炼分析（历史与工作区）",
                        FilePath = "Storage/Modules/Design/SmartParsing/ContentRefinery",
                        IsDirectory = true,
                        RiskLevel = RiskLevel.Low,
                        WarningMessage = "清除提炼历史记录和工作区状态",
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
                Icon = "Icon.Settings",
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
                Icon = "Icon.CheckCircle",
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
                Icon = "Icon.Monitor",
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
                Icon = "Icon.Settings",
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
                Icon = "Icon.Settings",
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

            return modules;
        }

        private async System.Threading.Tasks.Task<List<CleanupModule>> BuildModulesFromStorageAsync(List<CleanupModule> overrideModules)
        {
            var result = new List<CleanupModule>();
            var storageRoot = StoragePathHelper.GetStorageRoot();

            var chineseNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Animation"] = "动画效果",
                ["Font"] = "字体配置",
                ["IntelligentGeneration"] = "智能生成历史",
                ["ThemeManagement"] = "主题管理",
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
                "Storage/Framework/Appearance/Animation", "Storage/Framework/Appearance/Font",
                "Storage/Framework/UI/Windows",
                "Storage/Services/Framework/AI",
            };

            var whitelistLookup = new Dictionary<string, (CleanupModule Module, CleanupItem Item)>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in overrideModules)
                foreach (var item in module.Items)
                    whitelistLookup[NormalizePath(item.FilePath)] = (module, item);

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

                        if (!await DirectoryHasRealDataAsync(level2Dir).ConfigureAwait(false)) continue;
                        if (protectedPaths.Contains(normalizedPath)) continue;

                        CleanupItem item;
                        if (whitelistLookup.TryGetValue(normalizedPath, out var match))
                        {
                            item = match.Item;
                        }
                        else
                        {
                            var jsonFiles = Directory.GetFiles(level2Dir, "*.json", SearchOption.AllDirectories);
                            var firstFileName = Path.GetFileName(jsonFiles.FirstOrDefault() ?? "");
                            var displayName = chineseNameMap.TryGetValue(firstFileName, out var fn)
                                ? fn
                                : chineseNameMap.TryGetValue(level2Name, out var dn)
                                    ? dn
                                    : level2Name;

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

                        if (!await FileHasRealDataAsync(file).ConfigureAwait(false)) continue;

                        CleanupItem item;
                        if (whitelistLookup.TryGetValue(normalizedPath, out var fileMatch))
                        {
                            item = fileMatch.Item;
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

                    result.Add(new CleanupModule
                    {
                        Id = $"{layer.ToLower()}_{level1Name.ToLower()}",
                        Name = moduleDisplayName,
                        Icon = whitelistModule?.Icon ?? GetDefaultIcon(layer),
                        Layer = layer,
                        IsDangerous = whitelistModule?.IsDangerous ?? false,
                        Items = moduleItems
                    });
                }
            }

            var projectsModule = await BuildProjectsModuleAsync(storageRoot, overrideModules).ConfigureAwait(false);
            if (projectsModule != null && projectsModule.Items.Count > 0)
                result.Add(projectsModule);

            return result;
        }

        private async System.Threading.Tasks.Task<CleanupModule?> BuildProjectsModuleAsync(string storageRoot, List<CleanupModule> overrideModules)
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
                var hasData = await ProjectsHaveDataAsync(projectDirs, item.FilePath).ConfigureAwait(false);
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

        private static async System.Threading.Tasks.Task<bool> ProjectsHaveDataAsync(string[] projectDirs, string templatePath)
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
                    if (await DirectoryHasRealDataAsync(targetPath).ConfigureAwait(false)) return true;
                }
                else if (File.Exists(targetPath))
                {
                    if (await FileHasRealDataAsync(targetPath).ConfigureAwait(false)) return true;
                }
            }

            return false;
        }

        private static async System.Threading.Tasks.Task<bool> DirectoryHasRealDataAsync(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return false;
            foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                if (await FileHasRealDataAsync(file).ConfigureAwait(false)) return true;
            }
            return false;
        }

        private static async System.Threading.Tasks.Task<bool> FileHasRealDataAsync(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0) return false;
            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var content = (await File.ReadAllTextAsync(filePath).ConfigureAwait(false)).Trim();
                    if (content == "[]" || content == "{}" || content == "null" || string.IsNullOrWhiteSpace(content))
                        return false;
                }
                catch { }
            }
            return true;
        }

        #endregion
    }
}

