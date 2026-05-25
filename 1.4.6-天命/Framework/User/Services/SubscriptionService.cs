using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TM.Framework.User.Services
{
    public class SubscriptionService
    {
        private readonly string _subscriptionFilePath;
        private SubscriptionData? _cachedData;
        private readonly object _lock = new object();
        private readonly ApiService _apiService;

        public SubscriptionService(ApiService apiService)
        {
            _subscriptionFilePath = StoragePathHelper.GetFilePath("Framework", "User/Services", "subscription.json");
            _apiService = apiService;
            LoadSubscriptionData();
        }

        #region 订阅状态

        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _cachedData?.IsActive ?? false;
                }
            }
        }

        public string PlanType
        {
            get
            {
                lock (_lock)
                {
                    return _cachedData?.PlanType ?? "free";
                }
            }
        }

        public int RemainingDays
        {
            get
            {
                lock (_lock)
                {
                    if (_cachedData?.EndTime == null || _cachedData.EndTime <= DateTime.Now)
                        return 0;
                    return (int)Math.Ceiling((_cachedData.EndTime.Value - DateTime.Now).TotalDays);
                }
            }
        }

        public DateTime? EndTime
        {
            get
            {
                lock (_lock)
                {
                    return _cachedData?.EndTime;
                }
            }
        }

        public SubscriptionData? GetSubscriptionInfo()
        {
            lock (_lock)
            {
                return _cachedData;
            }
        }

        #endregion

        #region API操作

        public async Task<SubscriptionData?> GetSubscriptionFromServerAsync()
        {
            try
            {
                var apiResult = await _apiService.GetSubscriptionAsync();
                if (apiResult.Success && apiResult.Data != null)
                {
                    lock (_lock)
                    {
                        _cachedData = new SubscriptionData
                        {
                            SubscriptionId = apiResult.Data.SubscriptionId,
                            UserId = apiResult.Data.UserId,
                            PlanType = apiResult.Data.PlanType,
                            StartTime = apiResult.Data.StartTime,
                            EndTime = apiResult.Data.EndTime,
                            IsActive = apiResult.Data.IsActive,
                            Source = apiResult.Data.Source
                        };
                        SaveSubscriptionData();
                    }

                    TM.App.Log($"[SubscriptionService] 订阅信息已从服务器同步: {_cachedData.PlanType}, 到期: {_cachedData.EndTime:yyyy-MM-dd}");
                    return _cachedData;
                }

                TM.App.Log($"[SubscriptionService] 服务器获取失败: {apiResult.Message}");
                return _cachedData;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 获取订阅信息失败: {ex.Message}");
                return _cachedData;
            }
        }

        public async Task<CardKeyActivationResult> ActivateCardKeyAsync(string cardKey)
        {
            if (string.IsNullOrWhiteSpace(cardKey))
            {
                return new CardKeyActivationResult
                {
                    Success = false,
                    ErrorMessage = "请输入卡密"
                };
            }

            try
            {
                var apiResult = await _apiService.ActivateCardKeyAsync(cardKey);
                if (apiResult.Success && apiResult.Data != null)
                {
                    lock (_lock)
                    {
                        _cachedData = new SubscriptionData
                        {
                            PlanType = apiResult.Data.Subscription.PlanType,
                            EndTime = apiResult.Data.NewExpireTime,
                            IsActive = apiResult.Data.Subscription.IsActive,
                            Source = "card_key"
                        };
                        SaveSubscriptionData();
                    }

                    TM.App.Log($"[SubscriptionService] 卡密续费成功: +{apiResult.Data.DaysAdded}天, 新到期时间: {apiResult.Data.NewExpireTime:yyyy-MM-dd}");

                    return new CardKeyActivationResult
                    {
                        Success = true,
                        DaysAdded = apiResult.Data.DaysAdded,
                        NewExpireTime = apiResult.Data.NewExpireTime,
                        Message = $"续费成功！已增加{apiResult.Data.DaysAdded}天会员时长"
                    };
                }

                var errorMessage = apiResult.Message ?? "续费失败";
                if (apiResult.ErrorCode == ApiErrorCodes.CARDKEY_INVALID)
                {
                    errorMessage = "卡密无效或已过期";
                }
                else if (apiResult.ErrorCode == ApiErrorCodes.CARDKEY_USED)
                {
                    errorMessage = "该卡密已被使用";
                }
                else if (apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR)
                {
                    errorMessage = "网络连接失败，请检查网络后重试";
                }

                TM.App.Log($"[SubscriptionService] 卡密续费失败: {errorMessage}");
                return new CardKeyActivationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    ErrorCode = apiResult.ErrorCode
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 卡密续费异常: {ex.Message}");
                return new CardKeyActivationResult
                {
                    Success = false,
                    ErrorMessage = $"续费失败: {ex.Message}"
                };
            }
        }

        public async Task<CardKeyActivationResult> RenewAccountAsync(string account, string cardKey)
        {
            try
            {
                TM.App.Log($"[SubscriptionService] 开始为账号续费: {account}");

                var apiResult = await _apiService.RenewAccountWithCardKeyAsync(account, cardKey);

                if (apiResult.Success && apiResult.Data != null)
                {
                    TM.App.Log($"[SubscriptionService] 续费成功: {account}, 增加 {apiResult.Data.DaysAdded} 天");
                    return new CardKeyActivationResult
                    {
                        Success = true,
                        Message = $"续费成功！已为账号增加 {apiResult.Data.DaysAdded} 天会员时长",
                        DaysAdded = apiResult.Data.DaysAdded
                    };
                }

                var errorMessage = apiResult.Message ?? "续费失败";
                TM.App.Log($"[SubscriptionService] 续费失败: {errorMessage}");
                return new CardKeyActivationResult
                {
                    Success = false,
                    ErrorMessage = errorMessage,
                    ErrorCode = apiResult.ErrorCode
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 续费异常: {ex.Message}");
                return new CardKeyActivationResult
                {
                    Success = false,
                    ErrorMessage = $"续费失败: {ex.Message}"
                };
            }
        }

        public async Task<List<ActivationHistoryItem>> GetActivationHistoryAsync()
        {
            try
            {
                var apiResult = await _apiService.GetActivationHistoryAsync();
                if (apiResult.Success && apiResult.Data != null)
                {
                    TM.App.Log($"[SubscriptionService] 获取到 {apiResult.Data.Records.Count} 条激活历史");
                    return apiResult.Data.Records;
                }

                TM.App.Log($"[SubscriptionService] 获取激活历史失败: {apiResult.Message}");
                return new List<ActivationHistoryItem>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 获取激活历史异常: {ex.Message}");
                return new List<ActivationHistoryItem>();
            }
        }

        #endregion

        #region 本地存储

        private void LoadSubscriptionData()
        {
            try
            {
                if (File.Exists(_subscriptionFilePath))
                {
                    var json = File.ReadAllText(_subscriptionFilePath);
                    _cachedData = JsonSerializer.Deserialize<SubscriptionData>(json);
                    TM.App.Log("[SubscriptionService] 订阅数据已加载");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 加载订阅数据失败: {ex.Message}");
            }
        }

        private void SaveSubscriptionData()
        {
            try
            {
                var directory = Path.GetDirectoryName(_subscriptionFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_cachedData, JsonHelper.Default);
                var tmpSub = _subscriptionFilePath + ".tmp";
                File.WriteAllText(tmpSub, json);
                File.Move(tmpSub, _subscriptionFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 保存订阅数据失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveSubscriptionDataAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_subscriptionFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_cachedData, JsonHelper.Default);
                var tmpSubA = _subscriptionFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpSubA, json);
                File.Move(tmpSubA, _subscriptionFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionService] 异步保存订阅数据失败: {ex.Message}");
            }
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _cachedData = null;
                if (File.Exists(_subscriptionFilePath))
                {
                    try
                    {
                        File.Delete(_subscriptionFilePath);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SubscriptionService] 删除订阅缓存文件失败: {ex.Message}");
                    }
                }
            }
        }

        #endregion
    }

    #region 数据模型

    public class SubscriptionData
    {
        [System.Text.Json.Serialization.JsonPropertyName("SubscriptionId")] public int? SubscriptionId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UserId")] public string UserId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PlanType")] public string? PlanType { get; set; } = "free";
        [System.Text.Json.Serialization.JsonPropertyName("StartTime")] public DateTime? StartTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EndTime")] public DateTime? EndTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsActive")] public bool IsActive { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Source")] public string? Source { get; set; } = string.Empty;
    }

    public class CardKeyActivationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("Success")] public bool Success { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DaysAdded")] public int DaysAdded { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("NewExpireTime")] public DateTime NewExpireTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Message")] public string? Message { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ErrorCode")] public string? ErrorCode { get; set; }
    }

    #endregion
}
