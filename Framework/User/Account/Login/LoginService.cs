using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.User.Profile.BasicInfo;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.Login
{
    public class LoginService
    {
        private readonly string _accountsFile;
        private readonly string _rememberedFile;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private readonly ApiService _apiService;
        private readonly AuthTokenManager _authTokenManager;
        private readonly BasicInfoSettings _basicInfoSettings;
        private readonly TM.Services.Framework.AI.BuiltInConfigSyncService _builtInConfigSync;

        public LoginService(
            ApiService apiService,
            AuthTokenManager authTokenManager,
            BasicInfoSettings basicInfoSettings,
            TM.Services.Framework.AI.BuiltInConfigSyncService builtInConfigSync)
        {
            _accountsFile = StoragePathHelper.GetFilePath("Framework", "User/Account/Login", "accounts.json");
            _rememberedFile = StoragePathHelper.GetFilePath("Framework", "User/Account/Login", "remembered.json");
            _apiService = apiService;
            _authTokenManager = authTokenManager;
            _basicInfoSettings = basicInfoSettings;
            _builtInConfigSync = builtInConfigSync;
        }

        public async Task<LoginVerifyResult> VerifyLoginAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "请输入用户名" };
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "请输入密码" };
            }

            const int maxRetries = 2;
            ApiResponse<LoginResult>? apiResult = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                apiResult = await _apiService.LoginAsync(new TM.Framework.User.Services.LoginRequest
                {
                    Username = username,
                    Password = password
                });

                if (apiResult.Success || (apiResult.ErrorCode != ApiErrorCodes.NETWORK_ERROR && apiResult.ErrorCode != ApiErrorCodes.NETWORK_TIMEOUT))
                    break;

                if (attempt < maxRetries)
                {
                    TM.App.Log($"[LoginService] 网络失败，{attempt + 1}/{maxRetries} 次重试...");
                    await Task.Delay(1000);
                }
            }

            if (apiResult!.Success && apiResult.Data != null)
            {
                _authTokenManager.SaveTokens(apiResult.Data);

                var syncTask = SyncAccountToLocalAsync(username, password);
                var switchTask = Task.Run(async () =>
                {
                    try
                    {
                        await _basicInfoSettings.SwitchUserAsync(username).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[LoginService] 登录后切换用户资料失败: {ex.Message}");
                    }
                });

                await Task.WhenAll(syncTask, switchTask);

                try
                {
                    await _builtInConfigSync.SyncAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LoginService] 天命模型配置同步异常: {ex.Message}");
                }

                TM.App.Log($"[LoginService] API登录成功: {username}");
                return new LoginVerifyResult { Success = true };
            }

            var errorMessage = apiResult.ErrorCode switch
            {
                ApiErrorCodes.NETWORK_ERROR => "网络连接失败，请检查网络后重试",
                ApiErrorCodes.NETWORK_TIMEOUT => "连接超时，请检查网络后重试",
                ApiErrorCodes.SERVER_UNAVAILABLE => "服务器维护中，请稍后再试",
                ApiErrorCodes.SERVER_ERROR => "服务器异常，请稍后再试",
                ApiErrorCodes.RATE_LIMITED => "操作过于频繁，请稍后再试",
                ApiErrorCodes.ACCOUNT_LOCKED => apiResult.Message ?? "账号已锁定，请稍后重试",
                ApiErrorCodes.ACCOUNT_DISABLED => apiResult.Message ?? "账号已被禁用",
                _ => apiResult.Message ?? "登录失败"
            };

            TM.App.Log($"[LoginService] 登录失败: [{apiResult.ErrorCode}] {errorMessage} - {username}");
            return new LoginVerifyResult { Success = false, ErrorMessage = errorMessage, ErrorCode = apiResult.ErrorCode };
        }

        private async Task SyncAccountToLocalAsync(string username, string password)
        {
            try
            {
                var accounts = await LoadAccountsAsync();
                var account = accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (account == null)
                {
                    var salt = GenerateSalt();
                    var hash = HashPassword(password, salt);
                    accounts.Add(new UserAccount
                    {
                        Username = username,
                        PasswordHash = hash,
                        Salt = salt,
                        CreatedTime = DateTime.Now,
                        LastLoginTime = DateTime.Now,
                        IsEnabled = true
                    });
                }
                else
                {
                    account.LastLoginTime = DateTime.Now;
                }

                await SaveAccountsAsync(accounts);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 同步本地账号缓存失败: {ex.Message}");
            }
        }

        public async Task<LoginVerifyResult> CreateAccountAsync(string username, string password, string? licenseKey, string? inviteCode = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "用户名不能为空" };
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "密码不能为空" };
            }

            if (username.Length < 3)
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "用户名至少3个字符" };
            }

            if (password.Length < 6)
            {
                return new LoginVerifyResult { Success = false, ErrorMessage = "密码至少6个字符" };
            }

            var apiResult = await _apiService.RegisterAsync(new RegisterRequest
            {
                Username = username,
                Password = password,
                CardKey = licenseKey ?? string.Empty,
                InviteCode = inviteCode
            });

            if (apiResult.Success && apiResult.Data != null)
            {
                _authTokenManager.SaveTokens(apiResult.Data);

                var syncTask = SyncAccountToLocalAsync(username, password);
                var profileTask = Task.Run(async () =>
                {
                    try
                    {
                        await _basicInfoSettings.EnsureProfileExistsAsync(username).ConfigureAwait(false);
                        await _basicInfoSettings.SwitchUserAsync(username).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[LoginService] 创建账号后初始化用户资料失败: {ex.Message}");
                    }
                });

                await Task.WhenAll(syncTask, profileTask);

                TM.App.Log($"[LoginService] API注册成功: {username}");
                return new LoginVerifyResult { Success = true, InviteCode = apiResult.Data.InviteCode };
            }

            var errorMessage = apiResult.Message ?? "注册失败";

            if (apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR)
            {
                errorMessage = "网络连接失败，请检查网络后重试";
            }
            else if (apiResult.ErrorCode == ApiErrorCodes.USERNAME_EXISTS)
            {
                errorMessage = "用户名已存在";
            }

            TM.App.Log($"[LoginService] 注册失败: {errorMessage} - {username}");
            return new LoginVerifyResult { Success = false, ErrorMessage = errorMessage, ErrorCode = apiResult.ErrorCode };
        }

        public void SaveRememberedAccount(string username)
        {
            SaveRememberedAccount(username, true, false, null);
        }

        public void SaveRememberedAccount(string username, bool rememberAccount, bool rememberPassword, string? encryptedPassword)
            => _ = SaveRememberedAccountAsync(username, rememberAccount, rememberPassword, encryptedPassword);

        public async System.Threading.Tasks.Task SaveRememberedAccountAsync(string username, bool rememberAccount, bool rememberPassword, string? encryptedPassword)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var remembered = new RememberedAccount
                {
                    Username = username,
                    RememberAccount = rememberAccount,
                    RememberPassword = rememberPassword,
                    EncryptedPassword = encryptedPassword,
                    LastLoginTime = DateTime.Now
                };

                var json = JsonSerializer.Serialize(remembered, JsonHelper.Default);
                var tmp = _rememberedFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var target = _rememberedFile;
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, target, overwrite: true);

                TM.App.Log($"[LoginService] 已保存记住的账号: {username}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 保存记住账号失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void ClearRememberedAccount()
        {
            try
            {
                if (File.Exists(_rememberedFile))
                {
                    File.Delete(_rememberedFile);
                    TM.App.Log("[LoginService] 已清除记住的账号");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 清除记住账号失败: {ex.Message}");
            }
        }

        public void ClearAllAccounts()
        {
            try
            {
                if (File.Exists(_accountsFile))
                {
                    File.Delete(_accountsFile);
                    TM.App.Log("[LoginService] 已清除所有账号记录");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 清除账号记录失败: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task<List<UserAccount>> GetAllAccountsAsync()
        {
            var accounts = await LoadAccountsAsync().ConfigureAwait(false);
            return accounts;
        }

        public async System.Threading.Tasks.Task<RememberedAccount?> GetRememberedAccountInfoAsync()
        {
            try
            {
                if (!File.Exists(_rememberedFile))
                    return null;
                var json = await File.ReadAllTextAsync(_rememberedFile).ConfigureAwait(false);
                var remembered = JsonSerializer.Deserialize<RememberedAccount>(json);
                if (remembered?.RememberAccount == true && !string.IsNullOrWhiteSpace(remembered.Username))
                    return remembered;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 异步读取记住账号失败: {ex.Message}");
            }
            return null;
        }

        public async System.Threading.Tasks.Task<string?> GetRememberedAccountAsync()
        {
            var info = await GetRememberedAccountInfoAsync().ConfigureAwait(false);
            return info?.Username;
        }

        private async Task<List<UserAccount>> LoadAccountsAsync()
        {
            try
            {
                if (File.Exists(_accountsFile))
                {
                    await using var stream = File.OpenRead(_accountsFile);
                    return await JsonSerializer.DeserializeAsync<List<UserAccount>>(stream) ?? new List<UserAccount>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 异步加载账号列表失败: {ex.Message}");
            }

            return new List<UserAccount>();
        }

        private async System.Threading.Tasks.Task SaveAccountsAsync(List<UserAccount> accounts)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var directory = Path.GetDirectoryName(_accountsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tmpAa = _accountsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpAa))
                {
                    await JsonSerializer.SerializeAsync(stream, accounts, JsonHelper.Default);
                }
                File.Move(tmpAa, _accountsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 异步保存账号列表失败: {ex.Message}");
                throw;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private string HashPassword(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100000, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            return Convert.ToBase64String(hash);
        }

        private string GenerateSalt()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }

    public class LoginVerifyResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorCode { get; set; }
        public string? InviteCode { get; set; }
    }
}
