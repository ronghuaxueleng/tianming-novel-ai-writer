using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Profile.BasicInfo
{
    public class UserProfileService
    {
        private readonly string _avatarDirectory;
        private readonly string _avatarFilePath;
        private readonly BasicInfoSettings _basicInfoSettings;

        public UserProfileService()
        {
            _avatarDirectory = StoragePathHelper.GetFrameworkStoragePath("User/Profile/BasicInfo/Avatars");
            _avatarFilePath = Path.Combine(_avatarDirectory, "avatar.png");
            _basicInfoSettings = ServiceLocator.Get<BasicInfoSettings>();

            TM.App.Log($"[UserProfileService] 头像目录: {_avatarDirectory}");
        }

        public string SaveAvatar(string sourceImagePath)
        {
            try
            {
                if (!File.Exists(sourceImagePath))
                {
                    TM.App.Log($"[UserProfileService] 源图片不存在: {sourceImagePath}");
                    return string.Empty;
                }

                if (!Directory.Exists(_avatarDirectory))
                {
                    Directory.CreateDirectory(_avatarDirectory);
                }

                File.Copy(sourceImagePath, _avatarFilePath, true);

                TM.App.Log($"[UserProfileService] 头像保存成功: {_avatarFilePath}");
                return _avatarFilePath;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UserProfileService] 保存头像失败: {ex.Message}");
                return string.Empty;
            }
        }

        public string GetAvatarPath()
        {
            if (File.Exists(_avatarFilePath))
            {
                return _avatarFilePath;
            }

            return string.Empty;
        }

        public bool DeleteAvatar()
        {
            try
            {
                if (File.Exists(_avatarFilePath))
                {
                    File.Delete(_avatarFilePath);
                    TM.App.Log("[UserProfileService] 头像已删除");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UserProfileService] 删除头像失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExportProfileAsync(string exportPath)
        {
            try
            {
                var settings = _basicInfoSettings;
                var profile = settings.GetProfileData();

                string json = JsonSerializer.Serialize(profile, JsonHelper.CnDefault);

                string? directory = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(exportPath, json);

                TM.App.Log($"[UserProfileService] 资料导出成功: {exportPath}");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UserProfileService] 导出资料失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ImportProfileAsync(string importPath)
        {
            try
            {
                if (!File.Exists(importPath))
                {
                    TM.App.Log($"[UserProfileService] 导入文件不存在: {importPath}");
                    return false;
                }

                string json = await File.ReadAllTextAsync(importPath);
                var profile = JsonSerializer.Deserialize<UserProfileData>(json);

                if (profile != null)
                {
                    var settings = _basicInfoSettings;
                    settings.SetProfileData(profile);
                    settings.SaveSettings();

                    TM.App.Log($"[UserProfileService] 资料导入成功: {importPath}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UserProfileService] 导入资料失败: {ex.Message}");
                return false;
            }
        }
    }
}

