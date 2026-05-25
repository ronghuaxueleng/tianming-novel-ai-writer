using System;

namespace TM.Framework.User.Account.LoginHistory
{
    public class LoginHistoryModel
    {
        public string Id { get; set; } = string.Empty;
        public DateTime LoginTime { get; set; }
        public string LoginTimeDisplay => LoginTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string IpAddress { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Browser { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public bool IsAbnormal { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public DateTime? LogoutTime { get; set; }
        public int SessionDuration { get; set; }
        public LoginRiskLevel RiskLevel { get; set; }
        public string RiskReason { get; set; } = string.Empty;

        public string StatusIcon => RiskLevel switch
        {
            LoginRiskLevel.Critical => "🔴",
            LoginRiskLevel.High => "🟠",
            LoginRiskLevel.Medium => "🟡",
            _ => "🟢"
        };

        public string StatusText => RiskLevel switch
        {
            LoginRiskLevel.Critical => "严重风险",
            LoginRiskLevel.High => "高风险",
            LoginRiskLevel.Medium => "中风险",
            _ => "正常"
        };

        public string SessionStatus => LogoutTime.HasValue ? "已结束" : "活动中";
        public string SessionDurationDisplay
        {
            get
            {
                if (LogoutTime.HasValue)
                    return FormatDuration(SessionDuration);
                return FormatDuration((int)(DateTime.Now - LoginTime).TotalSeconds);
            }
        }

        private string FormatDuration(int seconds)
        {
            if (seconds < 60)
                return $"{seconds}秒";
            if (seconds < 3600)
                return $"{seconds / 60}分钟";
            return $"{seconds / 3600}小时{(seconds % 3600) / 60}分钟";
        }
    }
}

