using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

public class VersionTestingService
{
    private readonly string _dataFilePath;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private List<TestVersionData> _testVersions = new();

    public VersionTestingService()
    {
        _dataFilePath = StoragePathHelper.GetFilePath(
            "Modules",
            "AIAssistant/PromptTools/VersionTesting",
            "test_versions.json");

        _ = System.Threading.Tasks.Task.Run(async () => await LoadDataAsync().ConfigureAwait(false));
    }

    public List<TestVersionData> GetAllVersions()
    {
        lock (_lock)
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

        lock (_lock) _testVersions.Add(version);
        SaveData().SafeFireAndForget(ex => TM.App.Log($"[VersionTestingService] {ex.Message}"));
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

        lock (_lock) _testVersions.Add(version);
        await SaveDataAsync();
        TM.App.Log($"[VersionTestingService] 异步添加测试版本: {version.Name}");
    }

    public void UpdateVersion(TestVersionData version)
    {
        ArgumentNullException.ThrowIfNull(version);

        lock (_lock)
        {
            var existing = _testVersions.FirstOrDefault(v => v.Id == version.Id);
            if (existing == null) return;

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
        }

        SaveData().SafeFireAndForget(ex => TM.App.Log($"[VersionTestingService] {ex.Message}"));
        TM.App.Log($"[VersionTestingService] 更新测试版本: {version.Name}");
    }

    public void DeleteVersion(string id)
    {
        string? removedName = null;
        lock (_lock)
        {
            var version = _testVersions.FirstOrDefault(v => v.Id == id);
            if (version != null)
            {
                _testVersions.Remove(version);
                removedName = version.Name;
            }
        }
        if (removedName != null)
        {
            SaveData().SafeFireAndForget(ex => TM.App.Log($"[VersionTestingService] {ex.Message}"));
            TM.App.Log($"[VersionTestingService] 删除测试版本: {removedName}");
        }
    }

    public int ClearAllVersions()
    {
        int count;
        lock (_lock)
        {
            count = _testVersions.Count;
            _testVersions.Clear();
        }
        SaveData().SafeFireAndForget(ex => TM.App.Log($"[VersionTestingService] {ex.Message}"));
        TM.App.Log($"[VersionTestingService] 清空所有测试版本，共 {count} 个");
        return count;
    }

    private async Task LoadDataAsync()
    {
        if (File.Exists(_dataFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_dataFilePath).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<List<TestVersionData>>(json) ?? new List<TestVersionData>();
                lock (_lock) _testVersions = loaded;
                TM.App.Log($"[VersionTestingService] 加载测试版本: {_testVersions.Count} 个");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VersionTestingService] 加载失败: {ex.Message}");
                lock (_lock) _testVersions = new List<TestVersionData>();
            }
        }
        else
        {
            TM.App.Log("[VersionTestingService] 数据文件不存在，初始化空列表");
            lock (_lock) _testVersions = new List<TestVersionData>();
        }
    }

    private async Task SaveData()
    {
        List<TestVersionData> snapshot;
        lock (_lock) snapshot = _testVersions.ToList();

        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var tmpVt = _dataFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpVt))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonHelper.Default);
            }
            File.Move(tmpVt, _dataFilePath, overwrite: true);
            TM.App.Log($"[VersionTestingService] 保存测试版本: {snapshot.Count} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingService] 保存失败: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async System.Threading.Tasks.Task SaveDataAsync()
    {
        List<TestVersionData> snapshot;
        lock (_lock) snapshot = _testVersions.ToList();

        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var tmpVtA = _dataFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tmpVtA))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonHelper.Default);
            }
            File.Move(tmpVtA, _dataFilePath, overwrite: true);
            TM.App.Log($"[VersionTestingService] 异步保存测试版本: {snapshot.Count} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingService] 异步保存失败: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
