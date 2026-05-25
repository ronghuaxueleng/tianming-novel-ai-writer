using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.Storage;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Validate.ValidationSummary;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ValidationSummaryService : IValidationSummaryService
    {
        private const string ModulePath = "Validate/ValidationSummary";
        private const string DataDirectoryName = "data";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex VolumeNumberRegex = new(@"^第(\d+)卷", RegexOptions.Compiled);

        private readonly VolumeDesignService _volumeDesignService;
        private List<ValidationSummaryData> _dataItems;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private string DataDirectoryPath => Path.Combine(
            StoragePathHelper.GetModulesStoragePath(ModulePath),
            DataDirectoryName);

        public ValidationSummaryService(VolumeDesignService volumeDesignService)
        {
            _volumeDesignService = volumeDesignService;
            _dataItems = new List<ValidationSummaryData>();

            StoragePathHelper.EnsureDirectoryExists(DataDirectoryPath);

            _volumeDesignService.CategoryDeleted += OnVolumeCategoryDeleted;

            LoadData();
        }

        private void OnVolumeCategoryDeleted(object? sender, CategoryDeletedEventArgs e)
        {
            try
            {
                var categoryName = e.CategoryName;
                var dataToDelete = _dataItems.Where(d => d.Category == categoryName).ToList();

                if (dataToDelete.Count > 0)
                {
                    foreach (var item in dataToDelete)
                    {
                        _dataItems.Remove(item);
                        DeleteDataFile(item.Id);
                    }
                    TM.App.Log($"[ValidationSummaryService] 级联删除: 分类'{categoryName}'下的 {dataToDelete.Count} 条数据已删除");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 级联删除失败: {ex.Message}");
            }
        }

        #region 数据操作

        public List<ValidationSummaryData> GetAllData()
        {
            return _dataItems.ToList();
        }

        public ValidationSummaryData? GetDataById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return _dataItems.FirstOrDefault(d => d.Id == id);
        }

        public ValidationSummaryData? GetDataByVolumeNumber(int volumeNumber)
        {
            return _dataItems.FirstOrDefault(d => d.TargetVolumeNumber == volumeNumber);
        }

        public void AddData(ValidationSummaryData data)
        {
            if (data == null)
                return;

            if (string.IsNullOrEmpty(data.Id))
            {
                data.Id = ShortIdGenerator.New("D");
            }

            EnsureDataCategoryId(data);

            data.CreatedTime = DateTime.Now;
            data.ModifiedTime = DateTime.Now;

            _dataItems.Add(data);
            SaveDataItem(data);

            TM.App.Log($"[ValidationSummaryService] 添加数据: {data.Name}");
        }

        private void EnsureDataCategoryId(ValidationSummaryData data)
        {
            if (data == null)
                return;

            if (string.IsNullOrWhiteSpace(data.Category) || !string.IsNullOrWhiteSpace(data.CategoryId))
                return;

            var categories = GetAllCategories();
            var category = categories.FirstOrDefault(c => string.Equals(c.Name, data.Category, StringComparison.Ordinal));
            if (category == null || string.IsNullOrWhiteSpace(category.Id))
                return;

            data.CategoryId = category.Id;
        }

        public void UpdateData(ValidationSummaryData data)
        {
            if (data == null)
                return;

            data.ModifiedTime = DateTime.Now;
            SaveDataItem(data);

            TM.App.Log($"[ValidationSummaryService] 更新数据: {data.Name}");
        }

        public void DeleteData(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            var data = _dataItems.FirstOrDefault(d => d.Id == id);
            if (data != null)
            {
                _dataItems.Remove(data);
                DeleteDataFile(id);
                TM.App.Log($"[ValidationSummaryService] 删除数据: {data.Name}");
            }
        }

        #endregion

        #region 分类操作（订阅自VolumeDesignService）

        public List<ValidationSummaryCategory> GetAllCategories()
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .Select(v => new ValidationSummaryCategory
                {
                    Id = v.Id,
                    Name = v.VolumeNumber > 0 ? $"第{v.VolumeNumber}卷 {v.VolumeTitle}".Trim() : v.Name,
                    Icon = "📚",
                    Order = v.VolumeNumber,
                    IsBuiltIn = false,
                    IsEnabled = v.IsEnabled
                }).ToList();
        }

        #endregion

        #region 卷校验专用

        public void SaveVolumeValidation(int volumeNumber, ValidationSummaryData data)
        {
            if (data == null)
                return;

            var categories = GetAllCategories();
            var volumeCategory = categories.FirstOrDefault(c => c.Order == volumeNumber);
            var categoryName = volumeCategory?.Name ?? $"第{volumeNumber}卷";

            data.TargetVolumeNumber = volumeNumber;
            data.TargetVolumeName = categoryName;
            data.Name = $"{categoryName}校验";
            data.Category = categoryName;
            if (volumeCategory != null && !string.IsNullOrWhiteSpace(volumeCategory.Id))
            {
                data.CategoryId = volumeCategory.Id;
            }
            data.LastValidatedTime = DateTime.Now;

            var existingData = GetDataByVolumeNumber(volumeNumber);
            if (existingData != null)
            {
                data.Id = existingData.Id;
                data.CreatedTime = existingData.CreatedTime;
                data.ModifiedTime = DateTime.Now;

                var index = _dataItems.FindIndex(d => d.Id == existingData.Id);
                if (index >= 0)
                {
                    _dataItems[index] = data;
                }

                SaveDataItem(data);
                TM.App.Log($"[ValidationSummaryService] 覆盖更新卷校验: {data.Name}");
            }
            else
            {
                AddData(data);
                TM.App.Log($"[ValidationSummaryService] 新增卷校验: {data.Name}");
            }
        }

        public int ParseVolumeNumber(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName))
                return -1;

            var match = VolumeNumberRegex.Match(categoryName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int volumeNumber))
            {
                return volumeNumber;
            }

            return -1;
        }

        #endregion

        #region 数据持久化

        private void LoadData()
        {
            try
            {
                _dataItems.Clear();

                if (!Directory.Exists(DataDirectoryPath))
                {
                    return;
                }

                var files = Directory.GetFiles(DataDirectoryPath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var data = JsonSerializer.Deserialize<ValidationSummaryData>(json, JsonOptions);
                        if (data != null)
                        {
                            _dataItems.Add(data);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ValidationSummaryService] 加载数据文件失败: {file}, {ex.Message}");
                    }
                }

                TM.App.Log($"[ValidationSummaryService] 加载数据: {_dataItems.Count} 条");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 加载数据失败: {ex.Message}");
            }
        }

        private async void SaveDataItem(ValidationSummaryData data)
        {
            var acquired = false;
            try
            {
                await _saveLock.WaitAsync().ConfigureAwait(false);
                acquired = true;

                StoragePathHelper.EnsureDirectoryExists(DataDirectoryPath);

                var filePath = Path.Combine(DataDirectoryPath, $"{data.Id}.json");
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tmpVss = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpVss, json).ConfigureAwait(false);
                File.Move(tmpVss, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 保存数据失败: {data.Name}, {ex.Message}");
            }
            finally
            {
                if (acquired)
                    _saveLock.Release();
            }
        }

        private void DeleteDataFile(string id)
        {
            try
            {
                var filePath = Path.Combine(DataDirectoryPath, $"{id}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationSummaryService] 删除数据文件失败: {id}, {ex.Message}");
            }
        }

        #endregion
    }
}
