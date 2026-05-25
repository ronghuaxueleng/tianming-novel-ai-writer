using System;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.User.Profile.BasicInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class BasicInfoSettings : BaseSettings<BasicInfoSettings, UserProfileData>
    {
        public BasicInfoSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        private string GetDefaultProfileFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "User/Profile/BasicInfo", "user_profile.json");

        protected override string GetFilePath() =>
            GetDefaultProfileFilePath();

        protected override UserProfileData CreateDefaultData() => _objectFactory.Create<UserProfileData>();

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "user";

            var result = value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(result) ? "user" : result;
        }

        public string GetUserProfileFilePath(string username)
        {
            var safe = SanitizeFileName(username).ToLowerInvariant();
            return _storagePathHelper.GetFilePath("Framework", "User/Profile/BasicInfo/Profiles", $"{safe}.json");
        }

        public async Task EnsureProfileExistsAsync(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return;

                username = username.Trim();
                var path = GetUserProfileFilePath(username);
                if (File.Exists(path))
                    return;

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var profile = new UserProfileData
                {
                    Username = username,
                    DisplayName = "用户",
                    CreatedTime = DateTime.Now,
                    LastUpdatedTime = DateTime.Now
                };

                var json = JsonSerializer.Serialize(profile, JsonHelper.CnDefault);
                var tmpBis = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpBis, json).ConfigureAwait(false);
                File.Move(tmpBis, path, overwrite: true);

                TM.App.Log($"[BasicInfoSettings] 已创建用户资料文件: {username}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfoSettings] 创建用户资料文件失败: {ex.Message}");
            }
        }

        public void SwitchUser(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return;

                username = username.Trim();
                var targetPath = GetUserProfileFilePath(username);
                if (!string.Equals(FilePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    var defaultPath = GetDefaultProfileFilePath();
                    var previousData = Data;
                    var fromDefaultPath = string.Equals(FilePath, defaultPath, StringComparison.OrdinalIgnoreCase);
                    FilePath = targetPath;

                    if (File.Exists(FilePath))
                    {
                        _ = LoadDataAsync();
                    }
                    else
                    {
                        if (fromDefaultPath && previousData != null &&
                            string.Equals(previousData.Username, username, StringComparison.OrdinalIgnoreCase))
                        {
                            previousData.Username = username;
                            if (string.IsNullOrWhiteSpace(previousData.DisplayName) ||
                                string.Equals(previousData.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                                previousData.DisplayName = "用户";
                            Data = previousData;
                        }
                        else
                        {
                            Data = new UserProfileData
                            {
                                Username = username,
                                DisplayName = "用户",
                                CreatedTime = DateTime.Now,
                                LastUpdatedTime = DateTime.Now
                            };
                        }

                        _ = SaveDataAsync();
                    }
                }

                if (string.IsNullOrWhiteSpace(Data.DisplayName) ||
                    string.Equals(Data.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                {
                    Data.DisplayName = "用户";
                    _ = SaveDataAsync();
                }

                if (!string.Equals(Data.Username, username, StringComparison.Ordinal))
                {
                    Data.Username = username;
                    if (string.IsNullOrWhiteSpace(Data.DisplayName) ||
                        string.Equals(Data.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                        Data.DisplayName = "用户";
                    _ = SaveDataAsync();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfoSettings] 切换用户资料失败: {ex.Message}");
            }
        }

        public async System.Threading.Tasks.Task SwitchUserAsync(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return;

                username = username.Trim();
                var targetPath = GetUserProfileFilePath(username);
                if (!string.Equals(FilePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    var defaultPath = GetDefaultProfileFilePath();
                    var previousData = Data;
                    var fromDefaultPath = string.Equals(FilePath, defaultPath, StringComparison.OrdinalIgnoreCase);
                    FilePath = targetPath;

                    if (File.Exists(FilePath))
                    {
                        await LoadDataAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        if (fromDefaultPath && previousData != null &&
                            string.Equals(previousData.Username, username, StringComparison.OrdinalIgnoreCase))
                        {
                            previousData.Username = username;
                            if (string.IsNullOrWhiteSpace(previousData.DisplayName) ||
                                string.Equals(previousData.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                                previousData.DisplayName = "用户";
                            Data = previousData;
                        }
                        else
                        {
                            Data = new UserProfileData
                            {
                                Username = username,
                                DisplayName = "用户",
                                CreatedTime = DateTime.Now,
                                LastUpdatedTime = DateTime.Now
                            };
                        }

                        await SaveDataAsync().ConfigureAwait(false);
                    }
                }

                if (string.IsNullOrWhiteSpace(Data.DisplayName) ||
                    string.Equals(Data.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                {
                    Data.DisplayName = "用户";
                    await SaveDataAsync().ConfigureAwait(false);
                }

                if (!string.Equals(Data.Username, username, StringComparison.Ordinal))
                {
                    Data.Username = username;
                    if (string.IsNullOrWhiteSpace(Data.DisplayName) ||
                        string.Equals(Data.DisplayName, username, StringComparison.OrdinalIgnoreCase))
                        Data.DisplayName = "用户";
                    await SaveDataAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfoSettings] 异步切换用户资料失败: {ex.Message}");
            }
        }

        #region 便捷访问属性

        public string Username
        {
            get => Data.Username;
            set
            {
                if (Data.Username != value)
                {
                    Data.Username = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName
        {
            get => Data.DisplayName;
            set
            {
                if (Data.DisplayName != value)
                {
                    Data.DisplayName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RealName
        {
            get => Data.RealName;
            set
            {
                if (Data.RealName != value)
                {
                    Data.RealName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Gender
        {
            get => Data.Gender;
            set
            {
                if (Data.Gender != value)
                {
                    Data.Gender = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Email
        {
            get => Data.Email;
            set
            {
                if (Data.Email != value)
                {
                    Data.Email = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Phone
        {
            get => Data.Phone;
            set
            {
                if (Data.Phone != value)
                {
                    Data.Phone = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Country
        {
            get => Data.Country;
            set
            {
                if (Data.Country != value)
                {
                    Data.Country = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Province
        {
            get => Data.Province;
            set
            {
                if (Data.Province != value)
                {
                    Data.Province = value;
                    OnPropertyChanged();
                }
            }
        }

        public string City
        {
            get => Data.City;
            set
            {
                if (Data.City != value)
                {
                    Data.City = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AvatarPath
        {
            get => Data.AvatarPath;
            set
            {
                if (Data.AvatarPath != value)
                {
                    Data.AvatarPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Bio
        {
            get => Data.Bio;
            set
            {
                if (Data.Bio != value)
                {
                    Data.Bio = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime? Birthday
        {
            get => Data.Birthday;
            set
            {
                if (Data.Birthday != value)
                {
                    Data.Birthday = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime CreatedTime
        {
            get => Data.CreatedTime;
            set
            {
                if (Data.CreatedTime != value)
                {
                    Data.CreatedTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime LastUpdatedTime
        {
            get => Data.LastUpdatedTime;
            set
            {
                if (Data.LastUpdatedTime != value)
                {
                    Data.LastUpdatedTime = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 业务方法

        public override void SaveData()
        {
            Data.LastUpdatedTime = DateTime.Now;
            base.SaveData();
        }

        public override async System.Threading.Tasks.Task SaveDataAsync()
        {
            Data.LastUpdatedTime = DateTime.Now;
            await base.SaveDataAsync();
        }

        public UserProfileData GetProfileData() => Data;

        public void SetProfileData(UserProfileData profile)
        {
            Data = profile ?? new UserProfileData();
            SaveData();
        }

        public void SaveSettings() => SaveData();

        #endregion
    }
}
