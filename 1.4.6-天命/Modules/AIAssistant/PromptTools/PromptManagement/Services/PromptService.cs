using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;

public class PromptService : ModuleServiceBase<PromptCategory, PromptTemplateData>, IPromptRepository
{
    private const string BuiltInTemplatesFolder = "built_in_templates";
    private const string UserTemplatesFolder = "templates";

    private readonly string _builtInTemplatesPath;
    private readonly string _userTemplatesPath;

    private HashSet<string> _builtInTemplateIds = new(StringComparer.Ordinal);

    public static event EventHandler? TemplatesChanged;

    private static void RaiseTemplatesChanged()
    {
        try
        {
            TemplatesChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptService] 通知模板变更事件失败: {ex.Message}");
        }
    }

    public PromptService()
        : base(
            modulePath: "AIAssistant/PromptTools/PromptManagement",
            categoriesFileName: "categories.json",
            dataFileName: "templates.json",
            delayDataLoading: true)
    {
        _builtInTemplatesPath = StoragePathHelper.GetFilePath("Modules",
            "AIAssistant/PromptTools/PromptManagement", BuiltInTemplatesFolder);
        _userTemplatesPath = StoragePathHelper.GetFilePath("Modules",
            "AIAssistant/PromptTools/PromptManagement", UserTemplatesFolder);
    }

    protected override System.Threading.Tasks.Task OnAfterCategoriesLoadedAsync()
    {
        SetStorageStrategy(new DirectoryStorage<PromptTemplateData>(
            rootDir: _userTemplatesPath,
            filePathResolver: t => GetTemplateFilePath(t.Category),
            idResolver: t => t.Id,
            saveFilter: t => !t.IsBuiltIn));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    protected override async System.Threading.Tasks.Task OnInitializedAsync()
    {
        await LoadDataInternalAsync();

        await LoadAndMergeBuiltInTemplatesAsync();

        SeedBusinessPromptIfEmpty();

        RaiseTemplatesChanged();
    }

    private void SeedBusinessPromptIfEmpty()
    {
        const string categoryName = "业务提示词";
        try
        {
            var existing = GetAllData().Where(t => t.Category == categoryName).ToList();
            if (existing.Count > 0)
            {
                TM.App.Log($"[PromptService] 业务提示词分类已有 {existing.Count} 个模板，跳过种子");
                return;
            }

            var seed = new PromptTemplateData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "小说初稿生成器",
                Icon = "📋",
                Category = categoryName,
                IsEnabled = true,
                IsBuiltIn = false,
                IsDefault = true,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now,
                SystemPrompt = TM.Services.Framework.AI.SemanticKernel.Prompts.Business.BusinessPromptProvider.GenerationBusinessPrompt,
                UserTemplate = "",
                Variables = "",
                Tags = "章节生成,业务提示词,初稿",
                Description = "章节生成时注入的4级业务System Prompt，控制生成风格和质量。可直接修改，删除后自动回退到系统默认。"
            };

            AddTemplate(seed);
            TM.App.Log($"[PromptService] 已种子业务提示词默认模板: {seed.Id}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptService] 种子业务提示词失败: {ex.Message}");
        }
    }

    private string GetTemplateFilePath(string categoryName)
    {
        var category = Categories.FirstOrDefault(c => c.Name == categoryName);
        if (category == null)
        {
            TM.App.Log($"[PromptService] 分类 {categoryName} 未找到，使用默认路径");
            return StoragePathHelper.GetFilePath("Modules",
                "AIAssistant/PromptTools/PromptManagement",
                $"templates/common-root/{categoryName}.json");
        }

        if (category.Level == 2)
        {
            var parent = Categories.FirstOrDefault(c => c.Name == category.ParentCategory);
            var moduleId = parent?.Id ?? "common-root";
            return StoragePathHelper.GetFilePath("Modules",
                "AIAssistant/PromptTools/PromptManagement",
                $"templates/{moduleId}/{category.Id}.json");
        }

        var parentCategory = Categories.FirstOrDefault(c => c.Name == category.ParentCategory);
        var rootCategory = parentCategory != null
            ? Categories.FirstOrDefault(c => c.Name == parentCategory.ParentCategory)
            : null;

        var rootId = rootCategory?.Id ?? "common-root";
        var parentId = parentCategory?.Id;

        var subPath = string.IsNullOrEmpty(parentId)
            ? $"templates/{rootId}/{category.Id}.json"
            : $"templates/{rootId}/{parentId}/{category.Id}.json";

        return StoragePathHelper.GetFilePath("Modules",
            "AIAssistant/PromptTools/PromptManagement", subPath);
    }

    #region 双结构模板加载

    private async System.Threading.Tasks.Task LoadAndMergeBuiltInTemplatesAsync()
    {
        try
        {
            var builtInTemplates = await LoadBuiltInTemplatesAsync();
            if (builtInTemplates.Count == 0)
            {
                TM.App.Log($"[PromptService] 未找到系统内置模板或目录不存在");
                return;
            }

            _builtInTemplateIds = new HashSet<string>(
                builtInTemplates.Select(t => t.Id), 
                StringComparer.Ordinal);

            var userTemplateNames = DataItems
                .Select(t => $"{t.Category}:{t.Name}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int addedCount = 0;
            foreach (var builtIn in builtInTemplates)
            {
                var key = $"{builtIn.Category}:{builtIn.Name}";
                if (!userTemplateNames.Contains(key))
                {
                    builtIn.IsBuiltIn = true;
                    DataItems.Add(builtIn);
                    addedCount++;
                }
                else
                {
                    TM.App.Log($"[PromptService] 用户模板覆盖系统内置: {key}");
                }
            }

            TM.App.Log($"[PromptService] 合并系统内置模板: {addedCount} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptService] 加载系统内置模板失败: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task<List<PromptTemplateData>> LoadBuiltInTemplatesAsync()
    {
        var result = new List<PromptTemplateData>();

        if (!Directory.Exists(_builtInTemplatesPath))
        {
            TM.App.Log($"[PromptService] 系统内置模板目录不存在: {_builtInTemplatesPath}");
            return result;
        }

        try
        {
            var jsonFiles = Directory.GetFiles(_builtInTemplatesPath, "*.json", SearchOption.AllDirectories);

            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var templates = JsonSerializer.Deserialize<List<PromptTemplateData>>(json);
                    if (templates != null)
                    {
                        foreach (var template in templates)
                        {
                            template.IsBuiltIn = true;
                        }
                        result.AddRange(templates);
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PromptService] 加载内置模板文件失败 {file}: {ex.Message}");
                }
            }

            TM.App.Log($"[PromptService] 加载系统内置模板: {result.Count} 个");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptService] 遍历内置模板目录失败: {ex.Message}");
        }

        return result;
    }

    #endregion

    #region BuiltIn

    public bool IsBuiltInTemplate(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        if (_builtInTemplateIds.Contains(templateId))
        {
            return true;
        }

        var template = DataItems.FirstOrDefault(t => t.Id == templateId);
        return template?.IsBuiltIn ?? false;
    }

    public bool IsTemplateOperationAllowed(string templateId)
    {
        return !IsBuiltInTemplate(templateId);
    }

    public bool IsBuiltInCategory(string categoryName)
    {
        return IsCategoryBuiltIn(categoryName);
    }

    #endregion

    protected override int OnBeforeDeleteData(string dataId)
    {
        if (IsBuiltInTemplate(dataId))
        {
            TM.App.Log($"[PromptService] 系统内置模板不可删除: {dataId}");
            return 0;
        }

        return DataItems.RemoveAll(d => d.Id == dataId);
    }

    public IReadOnlyList<PromptTemplateData> GetAllTemplates() => GetAllData();

    public IReadOnlyList<PromptTemplateData> GetTemplatesByCategory(string categoryName)
    {
        return GetAllData().Where(t => t.Category == categoryName).ToList();
    }

    public PromptTemplateData? GetTemplateById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return GetAllData().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    }

    public void AddTemplate(PromptTemplateData template)
    {
        if (template == null) return;
        if (string.IsNullOrWhiteSpace(template.Id))
        {
            template.Id = ShortIdGenerator.New("D");
        }
        template.CreatedTime = DateTime.Now;
        template.ModifiedTime = DateTime.Now;
        AddData(template);
        RaiseTemplatesChanged();
    }

    public async System.Threading.Tasks.Task AddTemplateAsync(PromptTemplateData template)
    {
        if (template == null) return;
        if (string.IsNullOrWhiteSpace(template.Id))
        {
            template.Id = ShortIdGenerator.New("D");
        }
        template.CreatedTime = DateTime.Now;
        template.ModifiedTime = DateTime.Now;
        await AddDataAsync(template);
        RaiseTemplatesChanged();
    }

    public void UpdateTemplate(PromptTemplateData template)
    {
        if (template == null) return;

        if (IsBuiltInTemplate(template.Id))
        {
            TM.App.Log($"[PromptService] 系统内置模板不可修改: {template.Id}");
            return;
        }

        template.ModifiedTime = DateTime.Now;
        UpdateData(template);
        RaiseTemplatesChanged();
    }

    public void DeleteTemplate(string id)
    {
        if (IsBuiltInTemplate(id))
        {
            TM.App.Log($"[PromptService] 系统内置模板不可删除: {id}");
            return;
        }

        DeleteData(id);
        RaiseTemplatesChanged();
    }

    public int ClearAllTemplates()
    {
        var count = DataItems.Count;
        foreach (var item in DataItems.ToList())
        {
            DeleteData(item.Id);
        }
        if (count > 0)
        {
            RaiseTemplatesChanged();
        }
        return count;
    }

    IReadOnlyList<PromptCategory> IPromptRepository.GetAllCategories()
    {
        return GetAllCategories();
    }

    public PromptCategory? GetCategoryByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return GetAllCategories().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
    }
}
