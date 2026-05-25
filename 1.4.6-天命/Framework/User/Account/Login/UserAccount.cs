using System;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.User.Account.Login
{
    public class UserAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [System.Text.Json.Serialization.JsonPropertyName("Username")] public string Username { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PasswordHash")] public string PasswordHash { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Salt")] public string Salt { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonPropertyName("LastLoginTime")] public DateTime? LastLoginTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("DisplayName")] public string? DisplayName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Email")] public string? Email { get; set; }
    }

    public class RememberedAccount
    {
        [System.Text.Json.Serialization.JsonPropertyName("Username")] public string Username { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("RememberAccount")] public bool RememberAccount { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("RememberPassword")] public bool RememberPassword { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EncryptedPassword")] public string? EncryptedPassword { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LastLoginTime")] public DateTime LastLoginTime { get; set; } = DateTime.Now;
    }
}
