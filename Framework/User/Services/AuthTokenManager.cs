using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.User.Services
{
    public class AuthTokenManager
    {
        private readonly string _tokenFilePath;
        private AuthTokenData? _cachedTokenData;
        private readonly object _lock = new object();
        private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
        private readonly ServerAuthService _serverAuthService;

        private readonly byte[] _entropy;

        public AuthTokenManager()
        {
            _tokenFilePath = StoragePathHelper.GetFilePath("Framework", "User/Services", "auth_token.dat");
            _serverAuthService = ServiceLocator.Get<ServerAuthService>();
            _entropy = GenerateEntropy();
            _ = LoadTokenDataAsync();
        }

        private static byte[] GenerateEntropy()
        {
            var machineInfo = $"{Environment.MachineName}|{Environment.UserName}|TM-Token-Entropy";
            return SHA256.HashData(Encoding.UTF8.GetBytes(machineInfo))[..16];
        }

        public bool IsLoggedIn
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData != null &&
                           !string.IsNullOrEmpty(_cachedTokenData.AccessToken) &&
                           _cachedTokenData.ExpiresAt > DateTime.UtcNow;
                }
            }
        }

        public bool IsAccessTokenExpired
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData == null ||
                           _cachedTokenData.ExpiresAt <= DateTime.UtcNow;
                }
            }
        }

        public bool HasRefreshToken
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData != null &&
                           !string.IsNullOrEmpty(_cachedTokenData.RefreshToken);
                }
            }
        }

        public string? AccessToken
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData?.AccessToken;
                }
            }
        }

        public string? RefreshToken
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData?.RefreshToken;
                }
            }
        }

        public string? SessionKey
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData?.SessionKey;
                }
            }
        }

        public string? UserId
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData?.UserId;
                }
            }
        }

        public string? Username
        {
            get
            {
                lock (_lock)
                {
                    return _cachedTokenData?.Username;
                }
            }
        }

        public string ClientId
        {
            get
            {
                bool needsSave = false;
                string clientId;
                lock (_lock)
                {
                    if (_cachedTokenData == null || string.IsNullOrEmpty(_cachedTokenData.ClientId))
                    {
                        _cachedTokenData ??= new AuthTokenData();
                        _cachedTokenData.ClientId = ShortIdGenerator.NewGuid().ToString();
                        needsSave = true;
                    }
                    clientId = _cachedTokenData.ClientId;
                }
                if (needsSave) _ = SaveTokenDataAsync();
                return clientId;
            }
        }

        public void SaveTokens(LoginResult loginResult)
        {
            lock (_lock)
            {
                _cachedTokenData ??= new AuthTokenData();
                _cachedTokenData.AccessToken = loginResult.AccessToken;
                _cachedTokenData.RefreshToken = loginResult.RefreshToken;
                _cachedTokenData.SessionKey = loginResult.SessionKey;
                _cachedTokenData.ExpiresAt = loginResult.ExpiresAt;
                _cachedTokenData.UserId = loginResult.User.UserId;
                _cachedTokenData.Username = loginResult.User.Username;
                _cachedTokenData.LastLoginTime = DateTime.Now;
            }
            _ = SaveTokenDataAsync();
            TM.App.Log("[ATM] saved");
            _serverAuthService.SyncToken(loginResult.AccessToken, loginResult.ExpiresAt);
        }

        public void SaveTokens(RegisterResult registerResult)
        {
            lock (_lock)
            {
                _cachedTokenData ??= new AuthTokenData();
                _cachedTokenData.AccessToken = registerResult.AccessToken;
                _cachedTokenData.RefreshToken = registerResult.RefreshToken;
                _cachedTokenData.SessionKey = registerResult.SessionKey;
                _cachedTokenData.ExpiresAt = registerResult.ExpiresAt;
                _cachedTokenData.UserId = registerResult.UserId;
                _cachedTokenData.Username = registerResult.Username;
                _cachedTokenData.LastLoginTime = DateTime.Now;
            }
            _ = SaveTokenDataAsync();
            TM.App.Log("[ATM] saved");
            _serverAuthService.SyncToken(registerResult.AccessToken, registerResult.ExpiresAt);
        }

        public void UpdateTokens(RefreshTokenResult refreshResult)
        {
            bool hasData;
            lock (_lock)
            {
                hasData = _cachedTokenData != null;
                if (hasData)
                {
                    _cachedTokenData!.AccessToken = refreshResult.AccessToken;
                    _cachedTokenData.RefreshToken = refreshResult.RefreshToken;
                    _cachedTokenData.SessionKey = refreshResult.SessionKey;
                    _cachedTokenData.ExpiresAt = refreshResult.ExpiresAt;
                }
            }
            if (!hasData) { TM.App.Log("[ATM] upd err"); return; }
            _ = SaveTokenDataAsync();
            TM.App.Log("[ATM] refreshed");
            _serverAuthService.SyncToken(refreshResult.AccessToken, refreshResult.ExpiresAt);
        }

        public void ClearTokens()
        {
            lock (_lock)
            {
                var clientId = _cachedTokenData?.ClientId;
                _cachedTokenData = new AuthTokenData
                {
                    ClientId = clientId ?? ShortIdGenerator.NewGuid().ToString()
                };
            }
            _ = SaveTokenDataAsync();
            TM.App.Log("[ATM] cleared");
            _serverAuthService.ClearToken();
        }

        public SignatureHeaders GenerateSignatureHeaders(string method, string path, string? body = null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonce = ShortIdGenerator.NewGuid().ToString("N");
            var clientId = ClientId;

            var signContent = $"{method.ToUpper()}{path}{timestamp}{nonce}{body ?? ""}";

            var signature = ComputeHmacSha256(signContent, SessionKey ?? "");

            return new SignatureHeaders
            {
                ClientId = clientId,
                Timestamp = timestamp,
                Nonce = nonce,
                Signature = signature
            };
        }

        private string ComputeHmacSha256(string content, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToBase64String(hash);
        }

        private async System.Threading.Tasks.Task LoadTokenDataAsync()
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (File.Exists(_tokenFilePath))
                    {
                        var fileContent = await File.ReadAllTextAsync(_tokenFilePath).ConfigureAwait(false);

                        try
                        {
                            var encrypted = Convert.FromBase64String(fileContent);
                            var decrypted = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
                            var json = Encoding.UTF8.GetString(decrypted);
                            lock (_lock) { _cachedTokenData = JsonSerializer.Deserialize<AuthTokenData>(json); }
                            TM.App.Log("[ATM] loaded");
                            return;
                        }
                        catch (FormatException)
                        {
                        }
                        catch (CryptographicException)
                        {
                            TM.App.Log("[ATM] load err");
                        }

                        if (fileContent.TrimStart().StartsWith('{'))
                        {
                            lock (_lock) { _cachedTokenData = JsonSerializer.Deserialize<AuthTokenData>(fileContent); }
                            TM.App.Log("[ATM] migrated");
                            await SaveTokenDataAsync().ConfigureAwait(false);
                            return;
                        }
                    }

                    lock (_lock)
                    {
                        _cachedTokenData = new AuthTokenData { ClientId = ShortIdGenerator.NewGuid().ToString() };
                    }
                    await SaveTokenDataAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ATM] load err: {ex.Message}");
                    lock (_lock)
                    {
                        _cachedTokenData = new AuthTokenData { ClientId = ShortIdGenerator.NewGuid().ToString() };
                    }
                }
            }).ConfigureAwait(false);
        }

        private byte[] SerializeTokenDataSnapshot()
        {
            byte[] json;
            lock (_lock)
            {
                json = JsonSerializer.SerializeToUtf8Bytes(_cachedTokenData);
            }
            return ProtectedData.Protect(json, _entropy, DataProtectionScope.CurrentUser);
        }

        private async System.Threading.Tasks.Task SaveTokenDataAsync()
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var encrypted = await System.Threading.Tasks.Task.Run(SerializeTokenDataSnapshot).ConfigureAwait(false);
                var directory = Path.GetDirectoryName(_tokenFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                var tmp = _tokenFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, Convert.ToBase64String(encrypted)).ConfigureAwait(false);
                File.Move(tmp, _tokenFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ATM] save err: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }

    public class AuthTokenData
    {
        [System.Text.Json.Serialization.JsonPropertyName("AccessToken")] public string? AccessToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("RefreshToken")] public string? RefreshToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("SessionKey")] public string? SessionKey { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExpiresAt")] public DateTime ExpiresAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UserId")] public string? UserId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Username")] public string? Username { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ClientId")] public string? ClientId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LastLoginTime")] public DateTime LastLoginTime { get; set; }
    }

    public class SignatureHeaders
    {
        [System.Text.Json.Serialization.JsonPropertyName("ClientId")] public string ClientId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public string Timestamp { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Nonce")] public string Nonce { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Signature")] public string Signature { get; set; } = string.Empty;
    }
}
