using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

public class VersionTestingService
{
    private readonly string _dataFilePath;
    private List<TestVersionData> _testVersions = new();

    public VersionTestingService()
    {
        _dataFilePath = StoragePathHelper.GetFilePath(
            "Modules",
            "AIAssistant/PromptTools/VersionTesting",
            "test_versions.json");

        LoadData();
    }

    public List<TestVersionData> GetAllVersions()
    {
        return _testVersions.OrderByDescending(v => v.CreatedTime).ToList();
    }

    public void AddVersion(TestVersionData version)
    {
        if (version == null || string.IsNullOrWhiteSpace(version.Name))
        {
            throw new ArgumentException("版本名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(version.Id))
        {
            version.Id = ShortIdGenerator.New("D");
        }
        version.CreatedTime = DateTime.Now;
        version.ModifiedTime = DateTime.Now;

        _testVersions.Add(version);
        SaveData();
        TM.App.Log($"[VersionTestingService] 添加测试版本: {version.Name}");
    }

    public async System.Threading.Tasks.Task AddVersionAsync(TestVersionData version)
    {
        if (version == null || string.IsNullOrWhiteSpace(version.Name))
        {
            throw new ArgumentException("版本名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(version.Id))
        {
            version.Id = ShortIdGenerator.New("D");
        }
        version.CreatedTime = DateTime.Now;
        version.ModifiedTime = DateTime.Now;

        _testVersions.Add(version);
        await SaveDataAsync();
        TM.App.Log($"[VersionTestingService] 异步添加测试版本: {version.Name}");
    }

    public void UpdateVersion(TestVersionData version)
    {
        if (version == null) throw new ArgumentNullException(nameof(version));

        var existing = _testVersions.FirstOrDefault(v => v.Id == version.Id);
        if (existing != null)
        {
            existing.Name = version.Name;
            existing.Category = version.Category;
            existing.PromptId = version.PromptId;
            existing.VersionNumber = version.VersionNumber;
            existing.Description = version.Description;
            existing.TestInput = version.TestInput;
            existing.ExpectedOutput = version.ExpectedOutput;
            existing.TestScenario = version.TestScenario;
            existing.ActualOutput = version.ActualOutput;
            existing.Rating = version.Rating;
            existing.TestNotes = version.TestNotes;
            existing.TestStatus = version.TestStatus;
            existing.TestTime = version.TestTime;
            existing.ModifiedTime = DateTime.Now;

            SaveData();
            TM.App.Log($"[VersionTestingService] 更新测试版本: {version.Name}");
        }
    }

    public void DeleteVersion(string id)
    {
        var version = _testVersions.FirstOrDefault(v => v.Id == id);
        if (version != null)
        {
            _testVersions.Remove(version);
            SaveData();
            TM.App.Log($"[VersionTestingService] 删除测试版本: {version.Name}");
        }
    }

    public int ClearAllVersions()
    {
        int count = _testVersions.Count;
        _testVersions.Clear();
        SaveData();
        TM.App.Log($"[VersionTestingService] 清空所有测试版本，共 {count} 个");
        return count;
    }

    private void LoadData()
    {
        if (File.Exists(_dataFilePath))
        {
            try
            {
                var json = File.ReadAllText(_dataFilePath);
                _testVersions = JsonSerializer.Deserialize<List<TestVersionData>>(json) ?? new List<TestVersionData>();
                TM.App.Log($"[VersionTestingService] 加载测试版本: {_testVersions.Count} 个");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VersionTestingService] 加载失败: {ex.Message}");
                _testVersions = new List<TestVersionData>();
            }
        }
        else
        {
            TM.App.Log("[VersionTestingService] 数据文件不存在，初始化空列表");
            _testVersions = new List<TestVersionData>();
        }
    }

    private async void SaveData()
    {
        try
        {
            var json = JsonSerializer.Serialize(_testVersions, JsonHelper.Default);
            var tmpVt = _dataFilePath + ".tmp";
            await File.WriteAllTextAsync(tmpVt, json);
            File.Move(tmpVt, _dataFilePath, overwrite: true);
            TM.App.Log($"[VersionTestingService] 保存测试版本: {_testVersions.Count} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingService] 保存失败: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveDataAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_testVersions, JsonHelper.Default);
            var tmpVtA = _dataFilePath + ".tmp";
            await File.WriteAllTextAsync(tmpVtA, json);
            File.Move(tmpVtA, _dataFilePath, overwrite: true);
            TM.App.Log($"[VersionTestingService] 异步保存测试版本: {_testVersions.Count} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingService] 异步保存失败: {ex.Message}");
        }
    }
}
