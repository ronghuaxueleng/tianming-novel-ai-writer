using System;
using System.Text.Json.Serialization;

namespace TM.Framework.User.Profile.BasicInfo
{
    public class UserProfileData
    {
        [JsonPropertyName("Username")] public string Username { get; set; } = "User_" + DateTime.Now.Ticks.ToString().Substring(8);
        [JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("RealName")] public string RealName { get; set; } = string.Empty;
        [JsonPropertyName("Gender")] public string Gender { get; set; } = "保密";
        [JsonPropertyName("Email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("Phone")] public string Phone { get; set; } = string.Empty;
        [JsonPropertyName("Country")] public string Country { get; set; } = "中国";
        [JsonPropertyName("Province")] public string Province { get; set; } = string.Empty;
        [JsonPropertyName("City")] public string City { get; set; } = string.Empty;
        [JsonPropertyName("AvatarPath")] public string AvatarPath { get; set; } = string.Empty;
        [JsonPropertyName("Bio")] public string Bio { get; set; } = string.Empty;
        [JsonPropertyName("Birthday")] public DateTime? Birthday { get; set; }
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("LastUpdatedTime")] public DateTime LastUpdatedTime { get; set; } = DateTime.Now;
    }
}

