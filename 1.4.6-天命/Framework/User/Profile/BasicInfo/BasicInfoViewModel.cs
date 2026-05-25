using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Services;
using TM.Framework.User.Services;

namespace TM.Framework.User.Profile.BasicInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class BasicInfoViewModel : INotifyPropertyChanged
    {
        private readonly BasicInfoSettings _settings;
        private readonly UserProfileService _profileService;
        private readonly CurrentUserContext _userContext;
        private readonly AuthTokenManager _tokenManager;
        private readonly ApiService _apiService;

        private string _username = string.Empty;
        private string _displayName = string.Empty;
        private string _realName = string.Empty;
        private string _gender = "保密";
        private string _email = string.Empty;
        private string _phone = string.Empty;
        private string _country = "中国";
        private string _province = string.Empty;
        private string _city = string.Empty;
        private string _avatarPath = string.Empty;

        private string _emailError = string.Empty;
        private string _phoneError = string.Empty;

        public ObservableCollection<string> GenderOptions { get; } = new ObservableCollection<string>
        {
            "保密", "男", "女", "其他"
        };

        public ObservableCollection<string> CountryList { get; private set; }

        public ObservableCollection<string> ProvinceList { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public BasicInfoViewModel(
            BasicInfoSettings settings,
            UserProfileService profileService,
            CurrentUserContext userContext,
            AuthTokenManager tokenManager,
            ApiService apiService)
        {
            _settings = settings;
            _profileService = profileService;
            _userContext = userContext;
            _tokenManager = tokenManager;
            _apiService = apiService;

            CountryList = new ObservableCollection<string>(Framework.Common.Helpers.Utility.RegionDataHelper.GetCountries());
            ProvinceList = new ObservableCollection<string>();

            SaveCommand = new Framework.Common.Helpers.MVVM.RelayCommand(SaveProfile);
            ResetCommand = new Framework.Common.Helpers.MVVM.RelayCommand(ResetProfile);
            UploadAvatarCommand = new Framework.Common.Helpers.MVVM.RelayCommand(UploadAvatar);
            ExportDataCommand = new Framework.Common.Helpers.MVVM.RelayCommand(ExportData);
            ImportDataCommand = new Framework.Common.Helpers.MVVM.RelayCommand(ImportData);

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                _settings.LoadSettings();
                return () =>
                {
                    ApplyProfileToUI();
                    _ = TryPullProfileFromServerAsync();
                    _ = TrySyncPendingProfileAsync();
                };
            }, "BasicInfo");
        }

        #region 属性

        public string Username
        {
            get => _username;
            set
            {
                _username = value;
                OnPropertyChanged(nameof(Username));
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string RealName
        {
            get => _realName;
            set
            {
                _realName = value;
                OnPropertyChanged(nameof(RealName));
            }
        }

        public string Gender
        {
            get => _gender;
            set
            {
                _gender = value;
                OnPropertyChanged(nameof(Gender));
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged(nameof(Email));
                EmailError = Framework.Common.Helpers.Validation.ValidationHelper.GetEmailErrorMessage(value);
            }
        }

        public string Phone
        {
            get => _phone;
            set
            {
                _phone = value;
                OnPropertyChanged(nameof(Phone));
                PhoneError = Framework.Common.Helpers.Validation.ValidationHelper.GetPhoneErrorMessage(value);
            }
        }

        public string Country
        {
            get => _country;
            set
            {
                _country = value;
                OnPropertyChanged(nameof(Country));
                UpdateProvinceList();
            }
        }

        public string Province
        {
            get => _province;
            set
            {
                _province = value;
                OnPropertyChanged(nameof(Province));
            }
        }

        public string City
        {
            get => _city;
            set
            {
                _city = value;
                OnPropertyChanged(nameof(City));
            }
        }

        public string AvatarPath
        {
            get => _avatarPath;
            set
            {
                _avatarPath = value;
                OnPropertyChanged(nameof(AvatarPath));
            }
        }

        public string EmailError
        {
            get => _emailError;
            set
            {
                _emailError = value;
                OnPropertyChanged(nameof(EmailError));
                OnPropertyChanged(nameof(HasEmailError));
            }
        }

        public string PhoneError
        {
            get => _phoneError;
            set
            {
                _phoneError = value;
                OnPropertyChanged(nameof(PhoneError));
                OnPropertyChanged(nameof(HasPhoneError));
            }
        }

        public bool HasEmailError => !string.IsNullOrEmpty(EmailError);
        public bool HasPhoneError => !string.IsNullOrEmpty(PhoneError);

        #endregion

        #region 命令

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand UploadAvatarCommand { get; }
        public ICommand ExportDataCommand { get; }
        public ICommand ImportDataCommand { get; }

        #endregion

        #region 方法

        private void LoadProfile()
        {
            try
            {
                _settings.LoadSettings();
                ApplyProfileToUI();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 加载用户资料失败: {ex.Message}");
                StandardDialog.ShowError($"加载用户资料失败: {ex.Message}", "错误");
            }
        }

        private void ApplyProfileToUI()
        {
            Username = _settings.Username;
            DisplayName = _settings.DisplayName;
            RealName = _settings.RealName;
            Gender = _settings.Gender;
            Email = _settings.Email;
            Phone = _settings.Phone;
            Country = _settings.Country;
            Province = _settings.Province;
            City = _settings.City;
            AvatarPath = _settings.AvatarPath;

            UpdateProvinceList();

            TM.App.Log("[BasicInfo] 用户资料加载成功");
        }

        private void SaveProfile()
        {
            _ = SaveProfileAsync();
        }

        private async Task SaveProfileAsync()
        {
            try
            {
                if (HasEmailError || HasPhoneError)
                {
                    GlobalToast.Warning("验证失败", "请检查输入内容");
                    return;
                }

                _settings.Username = Username;
                _settings.DisplayName = DisplayName;
                _settings.RealName = RealName;
                _settings.Gender = Gender;
                _settings.Email = Email;
                _settings.Phone = Phone;
                _settings.Country = Country;
                _settings.Province = Province;
                _settings.City = City;
                _settings.AvatarPath = AvatarPath;

                _settings.SaveSettings();

                _userContext.Refresh();

                TM.App.Log("[BasicInfo] 用户资料保存成功");
                GlobalToast.Success("保存成功", "用户资料已保存");

                await TrySyncProfileToServerAsync();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 保存用户资料失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败: {ex.Message}", "错误");
            }
        }

        private string GetProfilePendingSyncFilePath()
        {
            return StoragePathHelper.GetFilePath("Framework", "User/Profile/BasicInfo", "profile_pending_sync.json");
        }

        private UserProfile BuildServerProfileDto()
        {
            var locationParts = new[] { Country, Province, City }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var location = string.Join("/", locationParts);

            return new UserProfile
            {
                UserId = _tokenManager.UserId ?? string.Empty,
                Username = Username,
                DisplayName = DisplayName,
                Email = string.IsNullOrWhiteSpace(Email) ? null : Email,
                Gender = Gender,
                Bio = string.IsNullOrWhiteSpace(_settings.Bio) ? null : _settings.Bio,
                Birthday = _settings.Birthday,
                Location = string.IsNullOrWhiteSpace(location) ? null : location
            };
        }

        private void ApplyServerProfileToLocal(UserProfile serverProfile)
        {
            if (!string.IsNullOrWhiteSpace(serverProfile.DisplayName))
            {
                DisplayName = serverProfile.DisplayName;
                _settings.DisplayName = serverProfile.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(serverProfile.Email))
            {
                Email = serverProfile.Email;
                _settings.Email = serverProfile.Email;
            }

            if (!string.IsNullOrWhiteSpace(serverProfile.Gender))
            {
                Gender = serverProfile.Gender;
                _settings.Gender = serverProfile.Gender;
            }

            if (!string.IsNullOrWhiteSpace(serverProfile.Bio))
            {
                _settings.Bio = serverProfile.Bio;
            }

            if (serverProfile.Birthday.HasValue)
            {
                _settings.Birthday = serverProfile.Birthday;
            }

            if (!string.IsNullOrWhiteSpace(serverProfile.Location))
            {
                var parts = serverProfile.Location.Split('/');
                if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    Country = parts[0];
                    _settings.Country = parts[0];
                }
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    Province = parts[1];
                    _settings.Province = parts[1];
                }
                if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                {
                    City = parts[2];
                    _settings.City = parts[2];
                }
            }

            _settings.SaveSettings();
            _userContext.Refresh();
        }

        private async Task TryPullProfileFromServerAsync()
        {
            try
            {
                if (!_tokenManager.HasRefreshToken)
                    return;

                var pendingPath = GetProfilePendingSyncFilePath();
                if (File.Exists(pendingPath))
                {
                    TM.App.Log("[BasicInfo] 存在待同步资料，跳过拉取服务器资料以避免覆盖本地修改");
                    return;
                }

                var apiResult = await _apiService.GetProfileAsync();
                if (!apiResult.Success)
                {
                    TM.App.Log($"[BasicInfo] 拉取服务器资料失败: {apiResult.ErrorCode} {apiResult.Message}");
                    return;
                }

                if (apiResult.Data != null)
                {
                    ApplyServerProfileToLocal(apiResult.Data);
                    TM.App.Log("[BasicInfo] 已从服务器同步资料到本地");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 拉取服务器资料异常: {ex.Message}");
            }
        }

        private async Task TrySyncProfileToServerAsync()
        {
            try
            {
                if (!_tokenManager.HasRefreshToken)
                    return;

                var profile = BuildServerProfileDto();
                var apiResult = await _apiService.UpdateProfileAsync(profile);

                if (apiResult.Success)
                {
                    var pendingPath = GetProfilePendingSyncFilePath();
                    if (File.Exists(pendingPath))
                        File.Delete(pendingPath);

                    TM.App.Log("[BasicInfo] 资料已同步到服务器");
                    return;
                }

                var errorMsg = apiResult.ErrorCode == ApiErrorCodes.NETWORK_ERROR
                    ? "网络连接失败，请检查网络后重试"
                    : (apiResult.Message ?? "服务器同步失败");

                var pending = new PendingProfileSync
                {
                    DisplayName = profile.DisplayName ?? string.Empty,
                    Email = profile.Email,
                    Gender = profile.Gender ?? "保密",
                    Bio = profile.Bio,
                    Birthday = profile.Birthday,
                    Location = profile.Location,
                    UpdatedAt = DateTime.Now
                };

                var json = JsonSerializer.Serialize(pending, JsonHelper.Default);
                var _pSyncPath = GetProfilePendingSyncFilePath();
                await Task.Run(() =>
                {
                    var tmpBiv = _pSyncPath + ".tmp";
                    File.WriteAllText(tmpBiv, json);
                    File.Move(tmpBiv, _pSyncPath, overwrite: true);
                });
                TM.App.Log($"[BasicInfo] 资料同步失败，已写入待同步队列: {errorMsg}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 同步资料到服务器异常: {ex.Message}");
            }
        }

        private async Task TrySyncPendingProfileAsync()
        {
            try
            {
                if (!_tokenManager.HasRefreshToken)
                    return;

                var path = GetProfilePendingSyncFilePath();

                var json = await Task.Run(() =>
                {
                    if (!File.Exists(path)) return (string?)null;
                    return File.ReadAllText(path);
                });
                if (json == null) return;

                var pending = JsonSerializer.Deserialize<PendingProfileSync>(json);
                if (pending == null)
                    return;

                var profile = BuildServerProfileDto();
                profile.DisplayName = pending.DisplayName;
                profile.Email = pending.Email;
                profile.Gender = pending.Gender;
                profile.Bio = pending.Bio;
                profile.Birthday = pending.Birthday;
                profile.Location = pending.Location;

                var apiResult = await _apiService.UpdateProfileAsync(profile);
                if (apiResult.Success)
                {
                    File.Delete(path);
                    TM.App.Log("[BasicInfo] 待同步资料已成功补同步到服务器");
                }
                else
                {
                    TM.App.Log($"[BasicInfo] 待同步资料补同步失败: {apiResult.ErrorCode} {apiResult.Message}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 补同步待同步资料异常: {ex.Message}");
            }
        }

        private class PendingProfileSync
        {
            [System.Text.Json.Serialization.JsonPropertyName("DisplayName")] public string DisplayName { get; set; } = string.Empty;
            [System.Text.Json.Serialization.JsonPropertyName("Email")] public string? Email { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Gender")] public string Gender { get; set; } = "保密";
            [System.Text.Json.Serialization.JsonPropertyName("Bio")] public string? Bio { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Birthday")] public DateTime? Birthday { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("Location")] public string? Location { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; }
        }

        private void ResetProfile()
        {
            var result = StandardDialog.ShowConfirm(
                "确定要重置吗？\n\n未保存的修改将丢失！",
                "确认重置"
            );

            if (result)
            {
                LoadProfile();
                TM.App.Log("[BasicInfo] 用户资料已重置");
                GlobalToast.Info("重置完成", "已恢复到保存的数据");
            }
        }

        private void UploadAvatar()
        {
            try
            {
                var dialog = new AvatarUploadDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);
                bool? result = dialog.ShowDialog();

                if (result == true && !string.IsNullOrEmpty(dialog.SelectedAvatarPath))
                {
                    string avatarPath = _profileService.SaveAvatar(dialog.SelectedAvatarPath);

                    if (!string.IsNullOrEmpty(avatarPath))
                    {
                        AvatarPath = avatarPath;
                        _settings.AvatarPath = avatarPath;
                        _settings.SaveSettings();

                        _userContext.Refresh();

                        TM.App.Log("[BasicInfo] 头像上传成功");
                        GlobalToast.Success("上传成功", "头像已更新");
                    }
                    else
                    {
                        StandardDialog.ShowError("头像保存失败", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 上传头像失败: {ex.Message}");
                StandardDialog.ShowError($"上传头像失败: {ex.Message}", "错误");
            }
        }

        private async void ExportData()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    FileName = $"UserProfile_{DateTime.Now:yyyyMMddHHmmss}.json",
                    Title = "导出用户资料"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    bool success = await _profileService.ExportProfileAsync(saveFileDialog.FileName);

                    if (success)
                    {
                        TM.App.Log($"[BasicInfo] 资料导出成功: {saveFileDialog.FileName}");
                        GlobalToast.Success("导出成功", "用户资料已导出");
                    }
                    else
                    {
                        StandardDialog.ShowError("导出失败", "错误");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 导出资料失败: {ex.Message}");
                StandardDialog.ShowError($"导出失败: {ex.Message}", "错误");
            }
        }

        private async void ImportData()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "JSON文件|*.json",
                    Title = "导入用户资料"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var result = StandardDialog.ShowConfirm(
                        "导入将覆盖当前资料，确定继续吗？",
                        "确认导入"
                    );

                    if (result)
                    {
                        bool success = await _profileService.ImportProfileAsync(openFileDialog.FileName);

                        if (success)
                        {
                            LoadProfile();

                            TM.App.Log($"[BasicInfo] 资料导入成功: {openFileDialog.FileName}");
                            GlobalToast.Success("导入成功", "用户资料已导入");
                        }
                        else
                        {
                            StandardDialog.ShowError("导入失败，文件格式可能不正确", "错误");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BasicInfo] 导入资料失败: {ex.Message}");
                StandardDialog.ShowError($"导入失败: {ex.Message}", "错误");
            }
        }

        private void UpdateProvinceList()
        {
            ProvinceList.Clear();

            var provinces = Framework.Common.Helpers.Utility.RegionDataHelper.GetProvinces(Country);
            foreach (var province in provinces)
            {
                ProvinceList.Add(province);
            }

            if (!ProvinceList.Contains(Province))
            {
                Province = string.Empty;
            }

            OnPropertyChanged(nameof(ProvinceList));
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

