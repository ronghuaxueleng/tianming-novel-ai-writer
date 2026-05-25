using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.AccountBinding
{
    public class AccountBindingService
    {
        private readonly ApiService _apiService;
        private readonly OAuthService _oAuthService;
        private readonly AccountBindingSettings _bindingSettings;

        public AccountBindingService(ApiService apiService, OAuthService oAuthService, AccountBindingSettings bindingSettings)
        {
            _apiService = apiService;
            _oAuthService = oAuthService;
            _bindingSettings = bindingSettings;
        }

        public List<ThirdPartyBinding> GetAllBindings()
        {
            var data = LoadBindingsData();
            return data.Bindings;
        }

        public async Task<List<ThirdPartyBinding>> GetAllBindingsFromServerAsync()
        {
            try
            {
                var apiResult = await _apiService.GetBindingsAsync();
                if (apiResult.Success && apiResult.Data != null)
                {
                    var bindings = new List<ThirdPartyBinding>();
                    foreach (var info in apiResult.Data.Bindings)
                    {
                        bindings.Add(new ThirdPartyBinding
                        {
                            Platform = ParsePlatformType(info.Platform),
                            AccountId = info.PlatformUserId,
                            Nickname = info.DisplayName ?? "",
                            BindTime = info.BoundTime,
                            IsActive = true,
                            SyncStatus = SyncStatus.Synced
                        });
                    }

                    var data = LoadBindingsData();
                    data.Bindings = bindings;
                    SaveBindingsData(data);

                    TM.App.Log($"[AccountBindingService] 从服务器获取到 {bindings.Count} 个绑定");
                    return bindings;
                }

                TM.App.Log($"[AccountBindingService] 服务器获取失败，使用本地缓存");
                return GetAllBindings();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 获取服务器绑定失败: {ex.Message}");
                return GetAllBindings();
            }
        }

        public async Task<BindingResult> BindAccountAsync(PlatformType platform, string code, string nickname, string email = "", string avatarUrl = "", List<string>? permissions = null)
        {
            try
            {
                var platformStr = platform.ToString().ToLower();
                var request = new OAuthRequest
                {
                    Platform = platformStr,
                    Code = code,
                    State = ShortIdGenerator.New("D")
                };

                var apiResult = await _apiService.BindAccountAsync(platformStr, request);
                if (!apiResult.Success || apiResult.Data == null)
                {
                    var errorMsg = apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR
                        ? "网络连接失败，请检查网络后重试"
                        : (apiResult.Message ?? "服务器同步失败");

                    TM.App.Log($"[AccountBindingService] API绑定失败: {errorMsg}");
                    return new BindingResult { Success = false, ErrorMessage = errorMsg };
                }

                var accountId = apiResult.Data.PlatformUserId;
                var displayName = string.IsNullOrWhiteSpace(apiResult.Data.DisplayName) ? nickname : apiResult.Data.DisplayName;

                var ok = BindAccount(platform, accountId, displayName, email, avatarUrl, permissions);
                if (!ok)
                {
                    return new BindingResult { Success = false, ErrorMessage = "本地缓存写入失败" };
                }

                TM.App.Log($"[AccountBindingService] 绑定成功: {platform} - {displayName}");
                return new BindingResult { Success = true };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 绑定异常: {ex.Message}");
                return new BindingResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private PlatformType ParsePlatformType(string platform)
        {
            return platform.ToLower() switch
            {
                "wechat" => PlatformType.WeChat,
                "qq" => PlatformType.QQ,
                "github" => PlatformType.GitHub,
                "google" => PlatformType.Google,
                "microsoft" => PlatformType.Microsoft,
                "baidu" => PlatformType.Baidu,
                _ => PlatformType.WeChat
            };
        }

        public ThirdPartyBinding? GetBinding(PlatformType platform)
        {
            var bindings = GetAllBindings();
            return bindings.FirstOrDefault(b => b.Platform == platform && b.IsActive);
        }

        public bool IsBound(PlatformType platform)
        {
            return GetBinding(platform) != null;
        }

        public bool BindAccount(PlatformType platform, string accountId, string nickname, string email = "", string avatarUrl = "", List<string>? permissions = null)
        {
            try
            {
                var data = LoadBindingsData();

                var existing = data.Bindings.FirstOrDefault(b => b.Platform == platform);
                var isUpdate = existing != null;

                if (existing != null)
                {
                    existing.AccountId = accountId;
                    existing.Nickname = nickname;
                    existing.Email = email;
                    existing.AvatarUrl = avatarUrl;
                    existing.BindTime = DateTime.Now;
                    existing.IsActive = true;
                    existing.Permissions = permissions ?? new List<string> { "basic_info", "profile" };
                    existing.LastUseTime = DateTime.Now;
                }
                else
                {
                    data.Bindings.Add(new ThirdPartyBinding
                    {
                        Platform = platform,
                        AccountId = accountId,
                        Nickname = nickname,
                        Email = email,
                        AvatarUrl = avatarUrl,
                        BindTime = DateTime.Now,
                        IsActive = true,
                        Permissions = permissions ?? new List<string> { "basic_info", "profile" },
                        LastUseTime = DateTime.Now,
                        SyncStatus = SyncStatus.Synced
                    });
                }

                AddHistoryRecord(data, platform, isUpdate ? BindingAction.Update : BindingAction.Bind, accountId, nickname, 
                    isUpdate ? "更新账号信息" : "首次绑定账号");

                SaveBindingsData(data);
                TM.App.Log($"[AccountBindingService] 绑定成功: {platform} - {nickname}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 绑定失败: {ex.Message}");
                return false;
            }
        }

        public bool BindAccount(PlatformType platform, string accountId, string nickname)
        {
            return BindAccount(platform, accountId, nickname, "", "", null);
        }

        public async Task<BindingResult> UnbindAccountAsync(PlatformType platform)
        {
            try
            {
                var platformStr = platform.ToString().ToLower();
                var apiResult = await _apiService.UnbindAccountAsync(platformStr);
                if (!apiResult.Success)
                {
                    var errorMsg = apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR
                        ? "网络连接失败，请检查网络后重试"
                        : (apiResult.Message ?? "服务器同步失败");
                    TM.App.Log($"[AccountBindingService] API解绑失败: {errorMsg}");
                    return new BindingResult { Success = false, ErrorMessage = errorMsg };
                }

                var data = LoadBindingsData();
                var binding = data.Bindings.FirstOrDefault(b => b.Platform == platform);

                if (binding != null)
                {
                    AddHistoryRecord(data, platform, BindingAction.Unbind, binding.AccountId, binding.Nickname, "用户主动解绑");
                    binding.IsActive = false;
                    SaveBindingsData(data);
                }

                TM.App.Log($"[AccountBindingService] 解绑成功: {platform}");
                return new BindingResult { Success = true };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 解绑失败: {ex.Message}");
                return new BindingResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public bool UpdateSyncStatus(PlatformType platform, SyncStatus status)
        {
            try
            {
                var data = LoadBindingsData();
                var binding = data.Bindings.FirstOrDefault(b => b.Platform == platform && b.IsActive);

                if (binding != null)
                {
                    binding.SyncStatus = status;
                    binding.LastSyncTime = DateTime.Now;
                    SaveBindingsData(data);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 更新同步状态失败: {ex.Message}");
                return false;
            }
        }

        public bool UpdatePermissions(PlatformType platform, List<string> permissions)
        {
            try
            {
                var data = LoadBindingsData();
                var binding = data.Bindings.FirstOrDefault(b => b.Platform == platform && b.IsActive);

                if (binding != null)
                {
                    binding.Permissions = permissions;
                    AddHistoryRecord(data, platform, BindingAction.PermissionChange, binding.AccountId, binding.Nickname, 
                        $"权限更新: {string.Join(", ", permissions)}");
                    SaveBindingsData(data);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 更新权限失败: {ex.Message}");
                return false;
            }
        }

        public List<BindingHistoryRecord> GetHistory(PlatformType? platform = null, int limit = 50)
        {
            var data = LoadBindingsData();
            var history = data.History;

            if (platform.HasValue)
            {
                history = history.Where(h => h.Platform == platform.Value).ToList();
            }

            return history.OrderByDescending(h => h.Timestamp).Take(limit).ToList();
        }

        public void RecordUsage(PlatformType platform)
        {
            try
            {
                var data = LoadBindingsData();
                var binding = data.Bindings.FirstOrDefault(b => b.Platform == platform && b.IsActive);

                if (binding != null)
                {
                    binding.LastUseTime = DateTime.Now;
                    SaveBindingsData(data);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingService] 记录使用时间失败: {ex.Message}");
            }
        }

        private void AddHistoryRecord(BindingsData data, PlatformType platform, BindingAction action, string accountId, string nickname, string details)
        {
            data.History.Add(new BindingHistoryRecord
            {
                Timestamp = DateTime.Now,
                Platform = platform,
                Action = action,
                AccountId = accountId,
                Nickname = nickname,
                Details = details
            });

            if (data.History.Count > 100)
            {
                data.History = data.History.OrderByDescending(h => h.Timestamp).Take(100).ToList();
            }
        }

        private BindingsData LoadBindingsData()
        {
            return _bindingSettings.LoadBindings();
        }

        private void SaveBindingsData(BindingsData data)
        {
            _bindingSettings.SaveBindings(data);
        }
    }

    #region 数据模型

    public class BindingsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Bindings")] public List<ThirdPartyBinding> Bindings { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("History")] public List<BindingHistoryRecord> History { get; set; } = new();
    }

    public class BindingHistoryRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Platform")] public PlatformType Platform { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Action")] public BindingAction Action { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AccountId")] public string AccountId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Nickname")] public string Nickname { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Details")] public string Details { get; set; } = string.Empty;
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum BindingAction
    {
        Bind,
        Unbind,
        Update,
        Sync,
        PermissionChange
    }

    public class ThirdPartyBinding
    {
        [System.Text.Json.Serialization.JsonPropertyName("Platform")] public PlatformType Platform { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AccountId")] public string AccountId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Nickname")] public string Nickname { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("AvatarUrl")] public string AvatarUrl { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Email")] public string Email { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BindTime")] public DateTime BindTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LastSyncTime")] public DateTime? LastSyncTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LastUseTime")] public DateTime? LastUseTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsActive")] public bool IsActive { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SyncStatus")] public SyncStatus SyncStatus { get; set; } = SyncStatus.None;
        [System.Text.Json.Serialization.JsonPropertyName("Permissions")] public List<string> Permissions { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ExtendedInfo")] public Dictionary<string, string> ExtendedInfo { get; set; } = new();
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum PlatformType
    {
        WeChat,
        QQ,
        GitHub,
        Google,
        Microsoft,
        Baidu,
        Weibo,
        Twitter
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum SyncStatus
    {
        None,
        Syncing,
        Synced,
        Failed,
        Outdated
    }

    public class BindingResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
    }

    #endregion
}

