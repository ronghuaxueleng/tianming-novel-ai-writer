using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TM.Framework.User.Services
{
    #region 通用响应模型

    public class ApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("message")]
        public string? Message { get; set; }
        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }
        [JsonPropertyName("serverTime")]
        public DateTime ServerTime { get; set; }

        public static ApiResponse Ok(string? message = null) => new ApiResponse
        {
            Success = true,
            Message = message,
            ServerTime = DateTime.UtcNow
        };

        public static ApiResponse Fail(string message, string? errorCode = null) => new ApiResponse
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            ServerTime = DateTime.UtcNow
        };
    }

    public class ApiResponse<T> : ApiResponse
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }

        public static ApiResponse<T> Ok(T data, string? message = null) => new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message,
            ServerTime = DateTime.UtcNow
        };

        public new static ApiResponse<T> Fail(string message, string? errorCode = null) => new ApiResponse<T>
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            ServerTime = DateTime.UtcNow
        };
    }

    #endregion

    #region 错误码常量

    public static class ApiErrorCodes
    {
        public const string AUTH_INVALID = "AUTH_INVALID";
        public const string AUTH_EXPIRED = "AUTH_EXPIRED";
        public const string AUTH_DEVICE_KICKED = "AUTH_DEVICE_KICKED";

        public const string RATE_LIMITED = "RATE_LIMITED";

        public const string CARDKEY_INVALID = "CARDKEY_INVALID";
        public const string CARDKEY_USED = "CARDKEY_USED";

        public const string BINDING_CONFLICT = "BINDING_CONFLICT";

        public const string ACCOUNT_LOCKED = "ACCOUNT_LOCKED";
        public const string ACCOUNT_DISABLED = "ACCOUNT_DISABLED";
        public const string USERNAME_EXISTS = "USERNAME_EXISTS";

        public const string SUBSCRIPTION_NONE = "SUBSCRIPTION_NONE";
        public const string SUBSCRIPTION_EXPIRED = "SUBSCRIPTION_EXPIRED";

        public const string NETWORK_ERROR = "NETWORK_ERROR";
        public const string SERVER_ERROR = "SERVER_ERROR";
        public const string INVALID_REQUEST = "INVALID_REQUEST";
    }

    #endregion

    #region 认证相关模型

    public class LoginRequest
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResult
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("sessionKey")]
        public string SessionKey { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("user")]
        public UserInfo User { get; set; } = new UserInfo();
    }

    public class RegisterRequest
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("cardKey")]
        public string CardKey { get; set; } = string.Empty;
    }

    public class RegisterResult
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("sessionKey")]
        public string SessionKey { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
    }

    public class RefreshTokenRequest
    {
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class RefreshTokenResult
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("sessionKey")]
        public string SessionKey { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
    }

    #endregion

    #region 用户信息模型

    public class UserInfo
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("avatarUrl")]
        public string? AvatarUrl { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";
        [JsonPropertyName("registerTime")]
        public DateTime RegisterTime { get; set; }
        [JsonPropertyName("lastLoginTime")]
        public DateTime? LastLoginTime { get; set; }
        [JsonPropertyName("subscription")]
        public SubscriptionInfo? Subscription { get; set; }
    }

    public class UserProfile
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";
        [JsonPropertyName("registerTime")]
        public DateTime RegisterTime { get; set; }
        [JsonPropertyName("lastLoginTime")]
        public DateTime? LastLoginTime { get; set; }
        [JsonPropertyName("avatarUrl")]
        public string? AvatarUrl { get; set; }
        [JsonPropertyName("bio")]
        public string? Bio { get; set; }
        [JsonPropertyName("location")]
        public string? Location { get; set; }
        [JsonPropertyName("birthday")]
        public DateTime? Birthday { get; set; }
        [JsonPropertyName("gender")]
        public string? Gender { get; set; }
    }

    #endregion

    #region 账号管理模型

    public class ChangePasswordRequest
    {
        [JsonPropertyName("oldPassword")]
        public string OldPassword { get; set; } = string.Empty;
        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class LockoutStatus
    {
        [JsonPropertyName("isLocked")]
        public bool IsLocked { get; set; }
        [JsonPropertyName("failedAttempts")]
        public int FailedAttempts { get; set; }
        [JsonPropertyName("lockedUntil")]
        public DateTime? LockedUntil { get; set; }
        [JsonPropertyName("isPermanentlyLocked")]
        public bool IsPermanentlyLocked { get; set; }
    }

    public class DeletionRequestDto
    {
        [JsonPropertyName("reasons")]
        public List<string> Reasons { get; set; } = new();
        [JsonPropertyName("customFeedback")]
        public string? CustomFeedback { get; set; }
        [JsonPropertyName("retainLoginHistory")]
        public bool RetainLoginHistory { get; set; }
        [JsonPropertyName("retainThemes")]
        public bool RetainThemes { get; set; }
        [JsonPropertyName("retainSettings")]
        public bool RetainSettings { get; set; }
    }

    public class DeletionStatusDto
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; } = string.Empty;
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        [JsonPropertyName("requestTime")]
        public DateTime RequestTime { get; set; }
        [JsonPropertyName("scheduledDeleteTime")]
        public DateTime ScheduledDeleteTime { get; set; }
        [JsonPropertyName("remainingDays")]
        public int RemainingDays { get; set; }
    }

    #endregion

    #region 登录历史模型

    public class LoginLogDto
    {
        [JsonPropertyName("logId")]
        public long LogId { get; set; }
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("loginTime")]
        public DateTime LoginTime { get; set; }
        [JsonPropertyName("ipAddress")]
        public string? IpAddress { get; set; }
        [JsonPropertyName("userAgent")]
        public string? UserAgent { get; set; }
        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;
        [JsonPropertyName("failReason")]
        public string? FailReason { get; set; }
        [JsonPropertyName("location")]
        public string? Location { get; set; }
    }

    public class LoginHistoryResult
    {
        [JsonPropertyName("records")]
        public List<LoginLogDto> Records { get; set; } = new();
        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
    }

    #endregion

    #region 第三方绑定模型

    public class BindingInfo
    {
        [JsonPropertyName("bindingId")]
        public long BindingId { get; set; }
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;
        [JsonPropertyName("platformUserId")]
        public string PlatformUserId { get; set; } = string.Empty;
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("boundTime")]
        public DateTime BoundTime { get; set; }
    }

    public class BindingsResult
    {
        [JsonPropertyName("bindings")]
        public List<BindingInfo> Bindings { get; set; } = new();
    }

    public class OAuthRequest
    {
        [JsonPropertyName("platform")]
        public string Platform { get; set; } = string.Empty;
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    public class OAuthLoginResult
    {
        [JsonPropertyName("isNewUser")]
        public bool IsNewUser { get; set; }
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("sessionKey")]
        public string SessionKey { get; set; } = string.Empty;
        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("user")]
        public UserInfo User { get; set; } = new UserInfo();
    }

    #endregion

    #region 会员订阅模型

    public class SubscriptionInfo
    {
        [JsonPropertyName("subscriptionId")]
        public int? SubscriptionId { get; set; }
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("planType")]
        public string? PlanType { get; set; } = "free";
        [JsonPropertyName("startTime")]
        public DateTime? StartTime { get; set; }
        [JsonPropertyName("endTime")]
        public DateTime? EndTime { get; set; }
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
        [JsonPropertyName("remainingDays")]
        public int RemainingDays { get; set; }
        [JsonPropertyName("source")]
        public string? Source { get; set; } = string.Empty;
    }

    public class ActivateCardKeyRequest
    {
        [JsonPropertyName("cardKey")]
        public string CardKey { get; set; } = string.Empty;
    }

    public class ActivationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("daysAdded")]
        public int DaysAdded { get; set; }
        [JsonPropertyName("newExpireTime")]
        public DateTime NewExpireTime { get; set; }
        [JsonPropertyName("subscription")]
        public SubscriptionInfo Subscription { get; set; } = new SubscriptionInfo();
    }

    public class ActivationHistoryItem
    {
        [JsonPropertyName("cardKey")]
        public string CardKey { get; set; } = string.Empty;
        [JsonPropertyName("durationDays")]
        public int DurationDays { get; set; }
        [JsonPropertyName("activatedTime")]
        public DateTime ActivatedTime { get; set; }
    }

    public class ActivationHistoryResult
    {
        [JsonPropertyName("records")]
        public List<ActivationHistoryItem> Records { get; set; } = new();
    }

    #endregion
}
