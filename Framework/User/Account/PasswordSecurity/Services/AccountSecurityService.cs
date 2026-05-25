using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TM.Framework.User.Account.PasswordSecurity.Services
{
    public class AccountSecurityService
    {
        private readonly AccountLockoutService _lockoutService;
        private readonly PasswordSecuritySettings _securitySettings;
        private volatile int _hasPasswordCache;

        public AccountSecurityService(AccountLockoutService lockoutService, PasswordSecuritySettings securitySettings)
        {
            _lockoutService = lockoutService;
            _securitySettings = securitySettings;
            _ = System.Threading.Tasks.Task.Run(async () => { try { var d = await _securitySettings.LoadPasswordDataAsync().ConfigureAwait(false); _hasPasswordCache = d != null ? 1 : -1; } catch { } });
        }

        #region R1

        public bool HasPassword()
        {
            var cache = _hasPasswordCache;
            if (cache != 0) return cache > 0;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var d = await _securitySettings.LoadPasswordDataAsync().ConfigureAwait(false);
                    _hasPasswordCache = d != null ? 1 : -1;
                }
                catch { }
            });
            return false;
        }

        public async System.Threading.Tasks.Task<bool> HasPasswordAsync()
        {
            if (_hasPasswordCache != 0) return _hasPasswordCache > 0;
            var result = await _securitySettings.LoadPasswordDataAsync().ConfigureAwait(false) != null;
            _hasPasswordCache = result ? 1 : -1;
            return result;
        }

        public async System.Threading.Tasks.Task<bool> VerifyPasswordAsync(string password)
        {
            var lockoutService = _lockoutService;
            if (await lockoutService.IsAccountLockedAsync())
            {
                TM.App.Log($"[ASS] locked: {await lockoutService.GetLockoutTimeRemainingAsync()}");
                return false;
            }
            var settings = _securitySettings;

            var (data, isValid, needsUpgrade, upgradedData) = await System.Threading.Tasks.Task.Run(async () =>
            {
                var d = await settings.LoadPasswordDataAsync().ConfigureAwait(false);
                if (d == null) return (d, false, false, (PasswordData?)null);

                bool sha256 = string.IsNullOrEmpty(d.HashAlgorithm) || d.HashAlgorithm == "SHA256";
                bool valid;
                bool upgrade = false;
                PasswordData? upgraded = null;

                if (sha256)
                {
                    var h = HashPasswordSHA256(password, d.Salt);
                    valid = h == d.PasswordHash;
                    if (valid)
                    {
                        var ns = GenerateSalt();
                        var nh = HashPasswordPBKDF2(password, ns, 100000);
                        upgraded = new PasswordData
                        {
                            PasswordHash = nh,
                            Salt = ns,
                            Iterations = 100000,
                            HashAlgorithm = "PBKDF2",
                            LastModifiedTime = DateTime.Now,
                            StrengthLevel = d.StrengthLevel
                        };
                        upgrade = true;
                    }
                }
                else
                {
                    var h = HashPasswordPBKDF2(password, d.Salt, d.Iterations);
                    valid = h == d.PasswordHash;
                }
                return (d, valid, upgrade, upgraded);
            }).ConfigureAwait(false);

            if (data == null) { await lockoutService.RecordFailedAttemptAsync(); return false; }

            if (needsUpgrade && upgradedData != null)
            {
                await settings.SavePasswordDataAsync(upgradedData).ConfigureAwait(false);
                TM.App.Log("[ASS] upg ok");
            }

            if (isValid) await lockoutService.ResetFailedAttemptsAsync();
            else await lockoutService.RecordFailedAttemptAsync();
            TM.App.Log(isValid ? "[ASS] ok" : "[ASS] fail");
            return isValid;
        }

        public async System.Threading.Tasks.Task<bool> ChangePasswordAsync(string oldPassword, string newPassword)
        {
            try
            {
                if (!await VerifyPasswordAsync(oldPassword).ConfigureAwait(false)) { TM.App.Log("[ASS] old fail"); return false; }
                if (await IsPasswordInHistoryAsync(newPassword).ConfigureAwait(false)) { TM.App.Log("[ASS] dup"); return false; }
                var salt = GenerateSalt();
                var hash = await System.Threading.Tasks.Task.Run(() => HashPasswordPBKDF2(newPassword, salt, 100000)).ConfigureAwait(false);
                var data = new PasswordData
                {
                    PasswordHash = hash,
                    Salt = salt,
                    Iterations = 100000,
                    HashAlgorithm = "PBKDF2",
                    LastModifiedTime = DateTime.Now,
                    StrengthLevel = TM.Framework.Common.Helpers.Validation.PasswordStrengthValidator.GetStrengthScore(newPassword)
                };
                await _securitySettings.SavePasswordDataAsync(data).ConfigureAwait(false);
                await AddToPasswordHistoryAsync(hash).ConfigureAwait(false);
                _hasPasswordCache = 1;
                TM.App.Log("[ASS] changed");
                return true;
            }
            catch (Exception ex) { TM.App.Log($"[ASS] chg err: {ex.Message}"); return false; }
        }

        public async System.Threading.Tasks.Task SetInitialPasswordAsync(string password)
        {
            var salt = GenerateSalt();
            var hash = await System.Threading.Tasks.Task.Run(() => HashPasswordPBKDF2(password, salt, 100000)).ConfigureAwait(false);
            var data = new PasswordData
            {
                PasswordHash = hash,
                Salt = salt,
                Iterations = 100000,
                HashAlgorithm = "PBKDF2",
                LastModifiedTime = DateTime.Now,
                StrengthLevel = TM.Framework.Common.Helpers.Validation.PasswordStrengthValidator.GetStrengthScore(password)
            };
            await _securitySettings.SavePasswordDataAsync(data).ConfigureAwait(false);
            await AddToPasswordHistoryAsync(hash).ConfigureAwait(false);
            _hasPasswordCache = 1;
            TM.App.Log("[ASS] init ok");
        }

        private async System.Threading.Tasks.Task AddToPasswordHistoryAsync(string passwordHash)
        {
            var settings = _securitySettings;
            var history = await settings.LoadPasswordHistoryAsync().ConfigureAwait(false);
            history.Add(passwordHash);
            if (history.Count > 5) history = history.Skip(history.Count - 5).ToList();
            await settings.SavePasswordHistoryAsync(history).ConfigureAwait(false);
        }

        #endregion

        #region R2

        private async System.Threading.Tasks.Task<bool> IsPasswordInHistoryAsync(string password)
        {
            var settings = _securitySettings;
            var history = await settings.LoadPasswordHistoryAsync().ConfigureAwait(false);
            var data = await settings.LoadPasswordDataAsync().ConfigureAwait(false);
            if (data == null) return false;
            var hash = HashPasswordPBKDF2(password, data.Salt, data.Iterations);
            return history.Contains(hash);
        }

        #endregion

        #region R3

        public async System.Threading.Tasks.Task<string> EnableTwoFactorAuthAsync()
        {
            var secret = GenerateTwoFactorSecret();
            var data = new TwoFactorAuthData { Secret = secret, IsEnabled = true, EnabledTime = DateTime.Now };
            await _securitySettings.SaveTwoFactorDataAsync(data).ConfigureAwait(false);
            TM.App.Log("[ASS] 2fa on");
            return secret;
        }

        public async System.Threading.Tasks.Task DisableTwoFactorAuthAsync()
        {
            var settings = _securitySettings;
            var data = await settings.LoadTwoFactorDataAsync().ConfigureAwait(false);
            if (data != null)
            {
                data.IsEnabled = false;
                await settings.SaveTwoFactorDataAsync(data).ConfigureAwait(false);
            }
            TM.App.Log("[ASS] 2fa off");
        }

        public async System.Threading.Tasks.Task<bool> IsTwoFactorEnabledAsync()
        {
            var data = await _securitySettings.LoadTwoFactorDataAsync().ConfigureAwait(false);
            return data?.IsEnabled ?? false;
        }

        public async System.Threading.Tasks.Task<string?> GetTwoFactorSecretAsync()
        {
            var data = await _securitySettings.LoadTwoFactorDataAsync().ConfigureAwait(false);
            return data?.Secret;
        }

        public async System.Threading.Tasks.Task<bool> VerifyTOTPCodeAsync(string code)
        {
            var data = await _securitySettings.LoadTwoFactorDataAsync().ConfigureAwait(false);
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
        [System.Text.Json.Serialization.JsonPropertyName("StrengthLevel")] public int StrengthLevel { get; set; } = 0;
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

