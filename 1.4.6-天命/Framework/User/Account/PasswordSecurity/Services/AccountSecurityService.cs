using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.PasswordSecurity.Services
{
    public class AccountSecurityService
    {
        private readonly AccountLockoutService _lockoutService;
        private readonly PasswordSecuritySettings _securitySettings;

        public AccountSecurityService(AccountLockoutService lockoutService, PasswordSecuritySettings securitySettings)
        {
            _lockoutService = lockoutService;
            _securitySettings = securitySettings;
        }

        #region R1

        public bool VerifyPassword(string password)
        {
            var lockoutService = _lockoutService;

            if (lockoutService.IsAccountLocked())
            {
                var remaining = lockoutService.GetLockoutTimeRemaining();
                TM.App.Log($"[ASS] locked: {remaining}");
                return false;
            }

            var settings = _securitySettings;
            var data = settings.LoadPasswordData();
            if (data == null)
            {
                lockoutService.RecordFailedAttempt();
                return false;
            }

            bool isSHA256 = string.IsNullOrEmpty(data.HashAlgorithm) || data.HashAlgorithm == "SHA256";

            string hash;
            bool isValid;

            if (isSHA256)
            {
                hash = HashPasswordSHA256(password, data.Salt);
                isValid = hash == data.PasswordHash;

                if (isValid)
                {
                    TM.App.Log("[ASS] upg");
                    var newSalt = GenerateSalt();
                    var newHash = HashPasswordPBKDF2(password, newSalt, 100000);

                    data.PasswordHash = newHash;
                    data.Salt = newSalt;
                    data.Iterations = 100000;
                    data.HashAlgorithm = "PBKDF2";
                    data.LastModifiedTime = DateTime.Now;

                    settings.SavePasswordData(data);
                    TM.App.Log("[ASS] upg ok");
                }
            }
            else
            {
                hash = HashPasswordPBKDF2(password, data.Salt, data.Iterations);
                isValid = hash == data.PasswordHash;
            }

            if (isValid)
            {
                lockoutService.ResetFailedAttempts();
                TM.App.Log("[ASS] ok");
            }
            else
            {
                lockoutService.RecordFailedAttempt();
                TM.App.Log("[ASS] fail");
            }

            return isValid;
        }

        public bool ChangePassword(string oldPassword, string newPassword)
        {
            try
            {
                if (!VerifyPassword(oldPassword))
                {
                    TM.App.Log("[ASS] old fail");
                    return false;
                }

                if (IsPasswordInHistory(newPassword))
                {
                    TM.App.Log("[ASS] dup");
                    return false;
                }

                var salt = GenerateSalt();
                var hash = HashPasswordPBKDF2(newPassword, salt, 100000);

                var data = new PasswordData
                {
                    PasswordHash = hash,
                    Salt = salt,
                    Iterations = 100000,
                    HashAlgorithm = "PBKDF2",
                    LastModifiedTime = DateTime.Now
                };
                var settings = _securitySettings;
                settings.SavePasswordData(data);

                AddToPasswordHistory(hash);

                TM.App.Log("[ASS] changed");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ASS] chg err: {ex.Message}");
                return false;
            }
        }

        public void SetInitialPassword(string password)
        {
            var salt = GenerateSalt();
            var hash = HashPasswordPBKDF2(password, salt, 100000);

            var data = new PasswordData
            {
                PasswordHash = hash,
                Salt = salt,
                Iterations = 100000,
                HashAlgorithm = "PBKDF2",
                LastModifiedTime = DateTime.Now
            };
            var settings = _securitySettings;
            settings.SavePasswordData(data);

            AddToPasswordHistory(hash);

            TM.App.Log("[ASS] init ok");
        }

        public bool HasPassword()
        {
            var settings = _securitySettings;
            return settings.LoadPasswordData() != null;
        }

        #endregion

        #region R2

        private bool IsPasswordInHistory(string password)
        {
            var settings = _securitySettings;
            var history = settings.LoadPasswordHistory();
            var data = settings.LoadPasswordData();
            if (data == null) return false;

            var hash = HashPasswordPBKDF2(password, data.Salt, data.Iterations);
            return history.Contains(hash);
        }

        private void AddToPasswordHistory(string passwordHash)
        {
            var settings = _securitySettings;
            var history = settings.LoadPasswordHistory();
            history.Add(passwordHash);

            if (history.Count > 5)
            {
                history = history.Skip(history.Count - 5).ToList();
            }

            settings.SavePasswordHistory(history);
        }

        #endregion

        #region R3

        public string EnableTwoFactorAuth()
        {
            var secret = GenerateTwoFactorSecret();
            var data = new TwoFactorAuthData
            {
                Secret = secret,
                IsEnabled = true,
                EnabledTime = DateTime.Now
            };
            var settings = _securitySettings;
            settings.SaveTwoFactorData(data);

            TM.App.Log("[ASS] 2fa on");
            return secret;
        }

        public void DisableTwoFactorAuth()
        {
            var settings = _securitySettings;
            var data = settings.LoadTwoFactorData();
            if (data != null)
            {
                data.IsEnabled = false;
                settings.SaveTwoFactorData(data);
            }

            TM.App.Log("[ASS] 2fa off");
        }

        public bool IsTwoFactorEnabled()
        {
            var settings = _securitySettings;
            var data = settings.LoadTwoFactorData();
            return data?.IsEnabled ?? false;
        }

        public string? GetTwoFactorSecret()
        {
            var settings = _securitySettings;
            var data = settings.LoadTwoFactorData();
            return data?.Secret;
        }

        public bool VerifyTOTPCode(string code)
        {
            var settings = _securitySettings;
            var data = settings.LoadTwoFactorData();
            if (data == null || !data.IsEnabled || string.IsNullOrEmpty(data.Secret))
                return false;

            long currentTimeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

            for (int i = -1; i <= 1; i++)
            {
                var generatedCode = GenerateTOTPCode(data.Secret, currentTimeStep + i);
                if (code == generatedCode)
                {
                    TM.App.Log("[ASS] 2fa ok");
                    return true;
                }
            }

            TM.App.Log("[ASS] 2fa fail");
            return false;
        }

        private string GenerateTwoFactorSecret()
        {
            var bytes = new byte[20];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes.ToBase32String();
        }

        private string GenerateTOTPCode(string secret, long timeStep = 0)
        {
            long counter = timeStep == 0 
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 
                : timeStep;

            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);

            var secretBytes = FromBase32String(secret);

            using var hmac = new HMACSHA1(secretBytes);
            var hash = hmac.ComputeHash(counterBytes);

            int offset = hash[hash.Length - 1] & 0x0F;
            int binary = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

            int otp = binary % 1000000;
            return otp.ToString("D6");
        }

        private byte[] FromBase32String(string base32)
        {
            const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            base32 = base32.TrimEnd('=').ToUpper();

            int bits = 0;
            int bitsRemaining = 0;
            var result = new System.Collections.Generic.List<byte>();

            foreach (char c in base32)
            {
                int value = base32Chars.IndexOf(c);
                if (value < 0) continue;

                bits = (bits << 5) | value;
                bitsRemaining += 5;

                if (bitsRemaining >= 8)
                {
                    result.Add((byte)(bits >> (bitsRemaining - 8)));
                    bitsRemaining -= 8;
                }
            }

            return result.ToArray();
        }

        #endregion

        #region R4

        private string HashPasswordPBKDF2(string password, string salt, int iterations = 100000)
        {
            var saltBytes = Convert.FromBase64String(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, iterations, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            return Convert.ToBase64String(hash);
        }

        private string HashPasswordSHA256(string password, string salt)
        {
            using var sha256 = SHA256.Create();
            var combined = password + salt;
            var bytes = Encoding.UTF8.GetBytes(combined);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private string HashPassword(string password, string salt, int iterations = 100000)
        {
            return HashPasswordPBKDF2(password, salt, iterations);
        }

        private string GenerateSalt()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        #endregion

    }

    #region R5

    public class PasswordData
    {
        [System.Text.Json.Serialization.JsonPropertyName("PasswordHash")] public string PasswordHash { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Salt")] public string Salt { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("LastModifiedTime")] public DateTime LastModifiedTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Iterations")] public int Iterations { get; set; } = 100000;
        [System.Text.Json.Serialization.JsonPropertyName("HashAlgorithm")] public string HashAlgorithm { get; set; } = "PBKDF2";
    }

    public class TwoFactorAuthData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Secret")] public string Secret { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("EnabledTime")] public DateTime EnabledTime { get; set; }
    }

    #endregion
}

internal static class Base32Extensions
{
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string ToBase32String(this byte[] bytes)
    {
        var result = new StringBuilder();
        int bits = 0;
        int bitsRemaining = 0;

        foreach (var b in bytes)
        {
            bits = (bits << 8) | b;
            bitsRemaining += 8;

            while (bitsRemaining >= 5)
            {
                var index = (bits >> (bitsRemaining - 5)) & 0x1F;
                result.Append(Base32Chars[index]);
                bitsRemaining -= 5;
            }
        }

        if (bitsRemaining > 0)
        {
            var index = (bits << (5 - bitsRemaining)) & 0x1F;
            result.Append(Base32Chars[index]);
        }

        return result.ToString();
    }
}

