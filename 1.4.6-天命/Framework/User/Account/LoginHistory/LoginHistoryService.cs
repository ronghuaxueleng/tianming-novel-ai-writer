using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.LoginHistory
{
    public class LoginHistoryService
    {
        private readonly ApiService _apiService;
        private readonly LoginHistorySettings _historySettings;

        public LoginHistoryService(ApiService apiService, LoginHistorySettings historySettings)
        {
            _apiService = apiService;
            _historySettings = historySettings;
        }

        public void RecordLogin(bool isSuccess = true)
        {
            _ = RecordLoginAsync(isSuccess);
        }

        public async Task RecordLoginAsync(bool isSuccess = true)
        {
            try
            {
                var sessionId = ShortIdGenerator.New("D");
                var ipAddress = IpLocationHelper.GetLocalIpAddress();

                var record = new LoginRecord
                {
                    Id = ShortIdGenerator.New("D"),
                    LoginTime = DateTime.Now,
                    IpAddress = ipAddress,
                    DeviceType = "Windows PC",
                    DeviceName = Environment.MachineName,
                    Location = IpLocationHelper.GetLocation(ipAddress),
                    Browser = "天命客户端",
                    OperatingSystem = Environment.OSVersion.ToString(),
                    IsSuccess = isSuccess,
                    SessionId = sessionId
                };

                var riskAssessment = AssessLoginRisk(record);
                record.IsAbnormal = riskAssessment.IsAbnormal;
                record.RiskLevel = riskAssessment.RiskLevel;
                record.RiskReason = riskAssessment.Reason;

                var data = LoadHistoryData();
                data.Records.Insert(0, record);

                if (data.Records.Count > 200)
                {
                    data.Records = data.Records.Take(200).ToList();
                }

                SaveHistoryData(data);
                TM.App.Log($"[LoginHistoryService] 登录记录已保存: {record.IpAddress} - {record.Location} [风险等级: {record.RiskLevel}]");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryService] 记录登录失败: {ex.Message}");
            }
        }

        public List<LoginRecord> GetAllRecords()
        {
            var data = LoadHistoryData();
            return data.Records;
        }

        public async Task<List<LoginRecord>> GetAllRecordsFromServerAsync()
        {
            try
            {
                var apiResult = await _apiService.GetLoginHistoryAsync();
                if (apiResult.Success && apiResult.Data != null)
                {
                    var local = GetAllRecords();

                    var server = new List<LoginRecord>();
                    foreach (var log in apiResult.Data.Records)
                    {
                        var r = new LoginRecord
                        {
                            Id = $"srv:{log.LogId}",
                            LoginTime = log.LoginTime,
                            IpAddress = log.IpAddress ?? "",
                            DeviceType = string.Empty,
                            DeviceName = string.Empty,
                            Location = log.Location ?? "",
                            Browser = log.UserAgent ?? "",
                            OperatingSystem = string.Empty,
                            IsSuccess = string.Equals(log.Result, "success", StringComparison.OrdinalIgnoreCase),
                            RiskReason = log.FailReason ?? string.Empty
                        };
                        r.ExtendedInfo["source"] = "server";
                        server.Add(r);
                    }

                    foreach (var r in local)
                    {
                        r.ExtendedInfo["source"] = "local";
                    }

                    string Key(LoginRecord r) => $"{r.LoginTime:O}|{r.IpAddress}|{r.IsSuccess}";

                    var localMap = local.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
                    var serverMap = server.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());

                    int serverOnly = 0;
                    int localOnly = 0;
                    int matched = 0;

                    var merged = new List<LoginRecord>();
                    foreach (var kv in serverMap)
                    {
                        if (localMap.TryGetValue(kv.Key, out var localItem))
                        {
                            matched++;
                            if (string.IsNullOrWhiteSpace(localItem.Location))
                                localItem.Location = kv.Value.Location;
                            if (string.IsNullOrWhiteSpace(localItem.RiskReason))
                                localItem.RiskReason = kv.Value.RiskReason;
                            merged.Add(localItem);
                        }
                        else
                        {
                            serverOnly++;
                            merged.Add(kv.Value);
                        }
                    }

                    foreach (var kv in localMap)
                    {
                        if (!serverMap.ContainsKey(kv.Key))
                        {
                            localOnly++;
                            merged.Add(kv.Value);
                        }
                    }

                    merged = merged.OrderByDescending(r => r.LoginTime).Take(400).ToList();

                    SaveHistoryData(new LoginHistoryData { Records = merged });
                    TM.App.Log($"[LoginHistoryService] 服务器记录={server.Count}，本地记录={local.Count}，匹配={matched}，仅服务器={serverOnly}，仅本地={localOnly}，合并后={merged.Count}");
                    return merged;
                }

                TM.App.Log($"[LoginHistoryService] 服务器获取失败，使用本地缓存");
                return GetAllRecords();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryService] 获取服务器历史失败: {ex.Message}");
                return GetAllRecords();
            }
        }

        public List<LoginRecord> GetFilteredRecords(DateTime? startDate = null, DateTime? endDate = null, string? deviceType = null)
        {
            var records = GetAllRecords();

            if (startDate.HasValue)
                records = records.Where(r => r.LoginTime >= startDate.Value).ToList();

            if (endDate.HasValue)
                records = records.Where(r => r.LoginTime <= endDate.Value).ToList();

            if (!string.IsNullOrEmpty(deviceType))
                records = records.Where(r => r.DeviceType == deviceType).ToList();

            return records;
        }

        public int GetAbnormalRecordsCount()
        {
            var records = GetAllRecords();
            return records.Count(r => r.IsAbnormal);
        }

        public DateTime? GetLastLoginTime()
        {
            var records = GetAllRecords();
            return records.FirstOrDefault()?.LoginTime;
        }

        public void ClearHistory()
        {
            var data = new LoginHistoryData { Records = new List<LoginRecord>() };
            SaveHistoryData(data);
            TM.App.Log("[LoginHistoryService] 历史记录已清空");
        }

        public string ExportHistory()
        {
            try
            {
                var exportPath = StoragePathHelper.GetFilePath("Framework", "User/Account/LoginHistory", $"login_history_export_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var data = LoadHistoryData();
                var json = JsonSerializer.Serialize(data, JsonHelper.Default);
                File.WriteAllText(exportPath, json);

                TM.App.Log($"[LoginHistoryService] 历史记录已导出: {exportPath}");
                return exportPath;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryService] 导出失败: {ex.Message}");
                return string.Empty;
            }
        }

        private (bool IsAbnormal, LoginRiskLevel RiskLevel, string Reason) AssessLoginRisk(LoginRecord currentRecord)
        {
            var records = GetAllRecords();
            if (records.Count == 0) 
                return (false, LoginRiskLevel.Low, "首次登录");

            var reasons = new List<string>();
            int riskScore = 0;

            var hour = currentRecord.LoginTime.Hour;
            if (hour >= 2 && hour < 6)
            {
                reasons.Add("深夜登录");
                riskScore += 20;
            }

            var recentRecords = records.Take(10).ToList();
            var recentIPs = recentRecords.Select(r => r.IpAddress).Distinct().ToList();
            if (!recentIPs.Contains(currentRecord.IpAddress))
            {
                reasons.Add("新IP地址");
                riskScore += 30;
            }

            var recentDevices = recentRecords.Select(r => r.DeviceName).Distinct().ToList();
            if (!recentDevices.Contains(currentRecord.DeviceName))
            {
                reasons.Add("新设备");
                riskScore += 25;
            }

            var lastRecord = records.FirstOrDefault();
            if (lastRecord != null && lastRecord.Location != currentRecord.Location)
            {
                var timeDiff = (currentRecord.LoginTime - lastRecord.LoginTime).TotalHours;
                if (timeDiff < 1)
                {
                    reasons.Add("异地快速登录");
                    riskScore += 40;
                }
            }

            var recentHourLogins = records.Count(r => (currentRecord.LoginTime - r.LoginTime).TotalHours < 1);
            if (recentHourLogins > 5)
            {
                reasons.Add("频繁登录");
                riskScore += 15;
            }

            LoginRiskLevel riskLevel;
            if (riskScore >= 60)
                riskLevel = LoginRiskLevel.Critical;
            else if (riskScore >= 40)
                riskLevel = LoginRiskLevel.High;
            else if (riskScore >= 20)
                riskLevel = LoginRiskLevel.Medium;
            else
                riskLevel = LoginRiskLevel.Low;

            bool isAbnormal = riskScore >= 30;
            string reason = reasons.Count > 0 ? string.Join(", ", reasons) : "正常";

            return (isAbnormal, riskLevel, reason);
        }

        public LoginStatistics GetStatistics(DateTime? startDate = null, DateTime? endDate = null)
        {
            var records = GetFilteredRecords(startDate, endDate, null);

            var stats = new LoginStatistics
            {
                TotalLogins = records.Count,
                SuccessfulLogins = records.Count(r => r.IsSuccess),
                FailedLogins = records.Count(r => !r.IsSuccess),
                AbnormalLogins = records.Count(r => r.IsAbnormal),
                UniqueIPs = records.Select(r => r.IpAddress).Distinct().Count(),
                UniqueDevices = records.Select(r => r.DeviceName).Distinct().Count()
            };

            stats.LocationDistribution = records
                .GroupBy(r => r.Location)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.HourlyDistribution = records
                .GroupBy(r => r.LoginTime.Hour)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.DeviceTypeDistribution = records
                .GroupBy(r => r.DeviceType)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.SecurityScore = CalculateSecurityScore(records);

            return stats;
        }

        private int CalculateSecurityScore(List<LoginRecord> records)
        {
            if (records.Count == 0) return 100;

            int score = 100;

            var abnormalRate = (double)records.Count(r => r.IsAbnormal) / records.Count;
            score -= (int)(abnormalRate * 30);

            var highRiskCount = records.Count(r => r.RiskLevel >= LoginRiskLevel.High);
            score -= highRiskCount * 5;

            var uniqueIPs = records.Select(r => r.IpAddress).Distinct().Count();
            if (uniqueIPs > records.Count / 2)
                score -= 15;

            var uniqueDevices = records.Select(r => r.DeviceName).Distinct().Count();
            if (uniqueDevices > 3)
                score -= 10;

            return Math.Max(0, Math.Min(100, score));
        }

        public List<LoginRecord> GetRiskRecords(LoginRiskLevel minRiskLevel = LoginRiskLevel.Medium)
        {
            var records = GetAllRecords();
            return records.Where(r => r.RiskLevel >= minRiskLevel).ToList();
        }

        public List<LoginRecord> GetActiveSessions()
        {
            var records = GetAllRecords();
            return records.Where(r => r.LogoutTime == null && 
                                    (DateTime.Now - r.LoginTime).TotalHours < 24).ToList();
        }

        public void EndSession(string sessionId)
        {
            try
            {
                var data = LoadHistoryData();
                var record = data.Records.FirstOrDefault(r => r.SessionId == sessionId);
                if (record != null)
                {
                    record.LogoutTime = DateTime.Now;
                    record.SessionDuration = (int)(DateTime.Now - record.LoginTime).TotalSeconds;
                    SaveHistoryData(data);
                    TM.App.Log($"[LoginHistoryService] 会话已结束: {sessionId}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryService] 结束会话失败: {ex.Message}");
            }
        }

        private LoginHistoryData LoadHistoryData()
        {
            return _historySettings.LoadRecords();
        }

        private void SaveHistoryData(LoginHistoryData data)
        {
            _historySettings.SaveRecords(data);
        }
    }

    #region 数据模型

    public class LoginHistoryData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Records")] public List<LoginRecord> Records { get; set; } = new();
    }

    public class LoginRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LoginTime")] public DateTime LoginTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IpAddress")] public string IpAddress { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DeviceType")] public string DeviceType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("DeviceName")] public string DeviceName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Location")] public string Location { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Browser")] public string Browser { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("OperatingSystem")] public string OperatingSystem { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsSuccess")] public bool IsSuccess { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsAbnormal")] public bool IsAbnormal { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SessionId")] public string SessionId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LogoutTime")] public DateTime? LogoutTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SessionDuration")] public int SessionDuration { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RiskLevel")] public LoginRiskLevel RiskLevel { get; set; } = LoginRiskLevel.Low;
        [System.Text.Json.Serialization.JsonPropertyName("RiskReason")] public string RiskReason { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ExtendedInfo")] public Dictionary<string, string> ExtendedInfo { get; set; } = new();
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum LoginRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class LoginStatistics
    {
        [System.Text.Json.Serialization.JsonPropertyName("TotalLogins")] public int TotalLogins { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SuccessfulLogins")] public int SuccessfulLogins { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("FailedLogins")] public int FailedLogins { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("AbnormalLogins")] public int AbnormalLogins { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UniqueIPs")] public int UniqueIPs { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UniqueDevices")] public int UniqueDevices { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LocationDistribution")] public Dictionary<string, int> LocationDistribution { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("HourlyDistribution")] public Dictionary<int, int> HourlyDistribution { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("DeviceTypeDistribution")] public Dictionary<string, int> DeviceTypeDistribution { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("SecurityScore")] public int SecurityScore { get; set; }
    }

    #endregion
}

