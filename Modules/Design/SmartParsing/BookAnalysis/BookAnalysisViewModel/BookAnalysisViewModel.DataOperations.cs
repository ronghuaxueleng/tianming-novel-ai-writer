using System;
using System.Collections.Generic;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    public partial class BookAnalysisViewModel
    {
        protected override IPromptRepository? GetPromptRepository() => _promptRepository;

        public List<string> GenreOptions { get; } = new();

        public List<string> StatusOptions { get; } = new()
        {
            "已禁用", "已启用"
        };

        protected override string DefaultDataIcon => "Icon.Book";

        protected override int GetMaxCategoryCount() => 1;
        protected override int GetMaxDataCountPerCategory() => 1;
        protected override string GetCategoryLimitMessage()
            => "智能拆书仅支持系统内置唯一分类，不允许新建分类。";
        protected override string GetDataLimitMessage()
            => "当前拆书分类已有数据，请先删除旧数据，再创建新的拆书内容。";

        protected override BookAnalysisData? CreateNewData(string? categoryName = null)
        {
            return new BookAnalysisData
            {
                Id = ShortIdGenerator.New("D"),
                Name = "新书籍",
                Category = categoryName ?? string.Empty,
                Icon = DefaultDataIcon,
                IsEnabled = true,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        protected override string? GetCurrentCategoryValue() => FormCategory;

        protected override void ApplyCategorySelection(string categoryName)
        {
            FormCategory = categoryName;
        }

        protected override int ClearAllDataItems()
        {
            try
            {
                var all = Service.GetAllAnalysis();
                foreach (var item in all)
                {
                    if (!string.IsNullOrWhiteSpace(item.Id))
                    {
                        _crawlerService.DeleteCrawledContent(item.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 清空前删除爬取内容失败: {ex.Message}");
            }

            return Service.ClearAllAnalysis();
        }

        protected override string GetModuleNameForVersionTracking() => "BookAnalysis";

        protected override List<BookAnalysisCategory> GetAllCategoriesFromService() => Service.GetAllCategories();

        protected override List<BookAnalysisData> GetAllDataItems() => Service.GetAllAnalysis();

        protected override string GetDataCategory(BookAnalysisData data) => data.Category;

        protected override TreeNodeItem ConvertToTreeNode(BookAnalysisData data)
        {
            return new TreeNodeItem
            {
                Name = data.Name,
                Icon = IconHelper.TryGet(data.Icon),
                Tag = data,
                ShowChildCount = false
            };
        }

        protected override string[] GetSearchAdditionalFields(BookAnalysisData data)
        {
            return new[] { data.Author, data.Genre, data.SourceBookTitle, data.SourceAuthor, data.SourceKeywords };
        }

        private ICommand? _selectNodeCommand;
        public ICommand SelectNodeCommand => _selectNodeCommand ??= new AsyncRelayCommand(async param =>
        {
            try
            {
                if (param is TreeNodeItem { Tag: BookAnalysisData data })
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);

                    OnDataItemLoaded();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();

                    if (!string.IsNullOrWhiteSpace(data.Id))
                    {
                        await LoadCrawledContent(data.Id);
                    }
                }
                else if (param is TreeNodeItem { Tag: BookAnalysisCategory category })
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    if (category.IsBuiltIn)
                    {
                        ResetForm();
                        EnterEditMode();
                    }
                    else
                    {
                        LoadCategoryToForm(category);
                        EnterEditMode();
                    }
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 节点选中失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
        });

        private void LoadDataToForm(BookAnalysisData data)
        {
            FormName = data.Name;
            FormIcon = data.Icon;
            FormStatus = data.IsEnabled ? "已启用" : "已禁用";
            FormCategory = data.Category;
            FormAuthor = data.Author;
            FormGenre = data.Genre;
            FormSourceUrl = data.SourceUrl;
            SourceBookTitle = data.SourceBookTitle;
            SourceAuthor = data.SourceAuthor;
            SourceGenre = data.SourceGenre;
            SourceKeywords = data.SourceKeywords;
            SourceSite = data.SourceSite;
            ChapterCount = data.ChapterCount;
            TotalWordCount = data.TotalWordCount;
            CrawledAt = data.CrawledAt;
            FormWorldBuildingMethod = data.WorldBuildingMethod;
            FormPowerSystemDesign = data.PowerSystemDesign;
            FormEnvironmentDescription = data.EnvironmentDescription;
            FormFactionDesign = data.FactionDesign;
            FormWorldviewHighlights = data.WorldviewHighlights;
            FormProtagonistDesign = data.ProtagonistDesign;
            FormSupportingRoles = data.SupportingRoles;
            FormCharacterRelations = data.CharacterRelations;
            FormGoldenFingerDesign = data.GoldenFingerDesign;
            FormCharacterHighlights = data.CharacterHighlights;
            FormPlotStructure = data.PlotStructure;
            FormConflictDesign = data.ConflictDesign;
            FormClimaxArrangement = data.ClimaxArrangement;
            FormForeshadowingTechnique = data.ForeshadowingTechnique;
            FormPlotHighlights = data.PlotHighlights;
        }

    }
}
