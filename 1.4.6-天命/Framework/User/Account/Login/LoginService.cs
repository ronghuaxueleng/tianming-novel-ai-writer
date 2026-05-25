using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading.Tasks;
using TM.Framework.User.Profile.BasicInfo;
using TM.Framework.User.Services;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.Login
{
    public class LoginService
    {
        private readonly string _accountsFile;
        private readonly string _rememberedFile;
        private readonly ApiService _apiService;
        private readonly AuthTokenManager _authTokenManager;
        private readonly BasicInfoSettings _basicInfoSettings;

        public LoginService(ApiService apiService, AuthTokenManager authTokenManager, BasicInfoSettings basicInfoSettings)
        {
            _accountsFile = StoragePathHelper.GetFilePath("Framework", "User/Account/Login", "accounts.json");
            _rememberedFile = StoragePathHelper.GetFilePath("Framework", "User/Account/Login", "remembered.json");
            _apiService = apiService;
            _authTokenManager = authTokenManager;
            _basicInfoSettings = basicInfoSettings;
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

            var apiResult = await _apiService.LoginAsync(new TM.Framework.User.Services.LoginRequest
            {
                Username = username,
                Password = password
            });

            if (apiResult.Success && apiResult.Data != null)
            {
                _authTokenManager.SaveTokens(apiResult.Data);

                var syncTask = SyncAccountToLocalAsync(username, password);
                var switchTask = Task.Run(() =>
                {
                    try
                    {
                        _basicInfoSettings.SwitchUser(username);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[LoginService] 登录后切换用户资料失败: {ex.Message}");
                    }
                });

                await Task.WhenAll(syncTask, switchTask);

                TM.App.Log($"[LoginService] API登录成功: {username}");
                return new LoginVerifyResult { Success = true };
            }

            var errorMessage = apiResult.Message ?? "登录失败";

            if (apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR)
            {
                errorMessage = "网络连接失败，请检查网络后重试";
            }

            TM.App.Log($"[LoginService] 登录失败: {errorMessage} - {username}");
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

        public async Task<LoginVerifyResult> CreateAccountAsync(string username, string password, string? licenseKey)
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
                CardKey = licenseKey ?? string.Empty
            });

            if (apiResult.Success && apiResult.Data != null)
            {
                _authTokenManager.SaveTokens(apiResult.Data);

                await SyncAccountToLocalAsync(username, password);

                try
                {
                    _basicInfoSettings.EnsureProfileExists(username);
                    _basicInfoSettings.SwitchUser(username);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[LoginService] 创建账号后初始化用户资料失败: {ex.Message}");
                }

                TM.App.Log($"[LoginService] API注册成功: {username}");
                return new LoginVerifyResult { Success = true };
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
        {
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
                var tmpR = _rememberedFile + ".tmp";
                File.WriteAllText(tmpR, json);
                File.Move(tmpR, _rememberedFile, overwrite: true);

                TM.App.Log($"[LoginService] 已保存记住的账号: {username}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 保存记住账号失败: {ex.Message}");
            }
        }

        public string? GetRememberedAccount()
        {
            try
            {
                if (File.Exists(_rememberedFile))
                {
                    var json = File.ReadAllText(_rememberedFile);
                    var remembered = JsonSerializer.Deserialize<RememberedAccount>(json);
                    if (remembered?.RememberAccount == true && !string.IsNullOrWhiteSpace(remembered.Username))
                    {
                        return remembered.Username;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 读取记住账号失败: {ex.Message}");
            }

            return null;
        }

        public RememberedAccount? GetRememberedAccountInfo()
        {
            try
            {
                if (!File.Exists(_rememberedFile))
                    return null;

                var json = File.ReadAllText(_rememberedFile);
                var remembered = JsonSerializer.Deserialize<RememberedAccount>(json);
                if (remembered == null)
                    return null;

                if (remembered.RememberAccount && !string.IsNullOrWhiteSpace(remembered.Username))
                    return remembered;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 读取记住账号失败: {ex.Message}");
            }

            return null;
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

        public bool HasAnyAccount()
        {
            var accounts = LoadAccounts();
            return accounts.Count > 0;
        }

        public List<UserAccount> GetAllAccounts()
        {
            return LoadAccounts();
        }

        private List<UserAccount> LoadAccounts()
        {
            try
            {
                if (File.Exists(_accountsFile))
                {
                    var json = File.ReadAllText(_accountsFile);
                    return JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 加载账号列表失败: {ex.Message}");
            }

            return new List<UserAccount>();
        }

        private async Task<List<UserAccount>> LoadAccountsAsync()
        {
            try
            {
                if (File.Exists(_accountsFile))
                {
                    var json = await File.ReadAllTextAsync(_accountsFile);
                    return JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 异步加载账号列表失败: {ex.Message}");
            }

            return new List<UserAccount>();
        }

        private void SaveAccounts(List<UserAccount> accounts)
        {
            try
            {
                var directory = Path.GetDirectoryName(_accountsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(accounts, JsonHelper.Default);
                var tmpA = _accountsFile + ".tmp";
                File.WriteAllText(tmpA, json);
                File.Move(tmpA, _accountsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 保存账号列表失败: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task SaveAccountsAsync(List<UserAccount> accounts)
        {
            try
            {
                var directory = Path.GetDirectoryName(_accountsFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(accounts, JsonHelper.Default);
                var tmpAa = _accountsFile + ".tmp";
                await File.WriteAllTextAsync(tmpAa, json);
                File.Move(tmpAa, _accountsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginService] 异步保存账号列表失败: {ex.Message}");
                throw;
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
    }
}
