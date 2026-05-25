using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    public partial class BookAnalysisViewModel
    {
        private async Task LoadCrawledContent(string dataId)
        {
            try
            {
                SelectedChapter = null;
                SelectedChapterContent = string.Empty;

                var content = await _crawlerService.LoadCrawledContentAsync(dataId);
                if (content != null)
                {
                    ChapterList.ReplaceAll(content.Chapters.Select(chapter => new Crawler.ChapterContent
                    {
                        Index = chapter.Index,
                        Title = chapter.Title,
                        FileName = chapter.FileName,
                        WordCount = chapter.WordCount,
                        Url = chapter.Url
                    }).ToList());
                }
                else
                {
                    ChapterList.ReplaceAll(System.Array.Empty<Crawler.ChapterContent>());
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 加载爬取内容失败: {ex.Message}");
            }
        }

        private void LoadCategoryToForm(BookAnalysisCategory category)
        {
            FormName = category.Name;
            FormIcon = category.Icon;
            FormStatus = "已启用";
            FormCategory = category.ParentCategory ?? string.Empty;
            ClearBusinessFields();
        }

        private void ResetForm()
        {
            FormName = string.Empty;
            FormIcon = DefaultDataIcon;
            FormStatus = "已启用";
            FormCategory = string.Empty;
            ClearBusinessFields();
        }

        private void ClearBusinessFields()
        {
            FormAuthor = string.Empty;
            FormGenre = string.Empty;
            FormSourceUrl = string.Empty;
            CurrentUrl = string.Empty;
            CrawlStatus = "未抓取";
            SourceBookTitle = string.Empty;
            SourceAuthor = string.Empty;
            SourceGenre = string.Empty;
            SourceKeywords = string.Empty;
            SourceSite = string.Empty;
            ChapterCount = 0;
            TotalWordCount = 0;
            CrawledAt = null;
            ChapterList.Clear();
            SelectedChapter = null;
            SelectedChapterContent = string.Empty;
            FormWorldBuildingMethod = string.Empty;
            FormPowerSystemDesign = string.Empty;
            FormEnvironmentDescription = string.Empty;
            FormFactionDesign = string.Empty;
            FormWorldviewHighlights = string.Empty;
            FormProtagonistDesign = string.Empty;
            FormSupportingRoles = string.Empty;
            FormCharacterRelations = string.Empty;
            FormGoldenFingerDesign = string.Empty;
            FormCharacterHighlights = string.Empty;
            FormPlotStructure = string.Empty;
            FormConflictDesign = string.Empty;
            FormClimaxArrangement = string.Empty;
            FormForeshadowingTechnique = string.Empty;
            FormPlotHighlights = string.Empty;
        }

        protected override string NewItemTypeName => "书籍分析";
        private ICommand? _addCommand;
        public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
        {
            try
            {
                _currentEditingData = null;
                _currentEditingCategory = null;
                ResetForm();
                ExecuteAddWithCreateMode();
                _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 新建失败: {ex.Message}");
                GlobalToast.Error("新建失败", $"新建失败：{ex.Message}");
            }
        });

        private ICommand? _saveCommand;
        public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
        {
            try
            {
                ExecuteSaveWithCreateEditMode(
                    validateForm: ValidateFormCore,
                    createCategoryCore: CreateCategoryCore,
                    createDataCore: CreateAnalysisCore,
                    hasEditingCategory: () => _currentEditingCategory != null,
                    hasEditingData: () => _currentEditingData != null,
                    updateCategoryCore: UpdateCategoryCore,
                    updateDataCore: UpdateAnalysisCore);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 保存失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
            }
        });

        private bool ValidateFormCore()
        {
            if (string.IsNullOrWhiteSpace(FormName))
            {
                GlobalToast.Warning("保存失败", "请输入书名");
                return false;
            }

            if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
            {
                GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或书籍分析");
                return false;
            }

            return true;
        }

        private void CreateCategoryCore()
        {
            var parentCategoryName = string.Empty;
            var level = 1;

            if (!string.IsNullOrWhiteSpace(FormCategory))
            {
                parentCategoryName = FormCategory;
                var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
                level = parentCategory != null ? parentCategory.Level + 1 : 1;
            }

            var categoryIcon = GetCategoryIconForSave(FormIcon);

            var newCategory = new BookAnalysisCategory
            {
                Id = ShortIdGenerator.New("C"),
                Name = FormName,
                Icon = categoryIcon,
                ParentCategory = parentCategoryName,
                Level = level,
                Order = Service.GetAllCategories().Count + 1
            };

            if (!Service.AddCategory(newCategory))
            {
                GlobalToast.Warning("创建失败", "分类名已存在，请改名");
                return;
            }

            string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
            GlobalToast.Success("保存成功", $"{levelDesc}『{newCategory.Name}』已创建");

            _currentEditingCategory = null;
            _currentEditingData = null;
            ResetForm();
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
        }

        private void CreateAnalysisCore()
        {
            if (string.IsNullOrWhiteSpace(FormCategory))
            {
                GlobalToast.Warning("保存失败", "请选择所属分类");
                return;
            }

            var newData = CreateNewData(FormCategory);
            if (newData == null) return;

            UpdateDataFromForm(newData);
            Service.AddAnalysis(newData);
            _currentEditingData = newData;
            _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
            GlobalToast.Success("保存成功", $"书籍分析『{newData.Name}』已创建");
        }

        private void UpdateCategoryCore()
        {
            if (_currentEditingCategory == null) return;

            var oldName = _currentEditingCategory.Name;
            _currentEditingCategory.Name = FormName;
            _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
            if (!Service.UpdateCategory(_currentEditingCategory))
            {
                _currentEditingCategory.Name = oldName;
                GlobalToast.Warning("保存失败", "分类名已存在，请改名");
                return;
            }
            GlobalToast.Success("保存成功", $"分类『{_currentEditingCategory.Name}』已更新");
        }

        private void UpdateAnalysisCore()
        {
            if (_currentEditingData == null) return;

            UpdateDataFromForm(_currentEditingData);
            Service.UpdateAnalysis(_currentEditingData);
            GlobalToast.Success("", $"『{_currentEditingData.Name}』");
        }

        private void UpdateDataFromForm(BookAnalysisData data)
        {
            var newIsEnabled = (FormStatus == "");
            if (newIsEnabled && !data.IsEnabled)
            {
                if (!CheckBeforeEnable(null, data.Name))
                {
                    FormStatus = "";
                    return;
                }
            }

            data.Name = FormName;
            data.Icon = GetDataIconForSave(FormIcon);
            data.Category = FormCategory;
            data.IsEnabled = newIsEnabled;
            data.ModifiedTime = DateTime.Now;
            data.Author = FormAuthor;
            data.Genre = FormGenre;
            data.SourceUrl = FormSourceUrl;
            data.SourceBookTitle = SourceBookTitle;
            data.SourceAuthor = SourceAuthor;
            data.SourceGenre = SourceGenre;
            data.SourceKeywords = SourceKeywords;
            data.SourceSite = SourceSite;
            data.ChapterCount = ChapterCount;
            data.TotalWordCount = TotalWordCount;
            data.CrawledAt = CrawledAt;
            data.WorldBuildingMethod = FormWorldBuildingMethod;
            data.PowerSystemDesign = FormPowerSystemDesign;
            data.EnvironmentDescription = FormEnvironmentDescription;
            data.FactionDesign = FormFactionDesign;
            data.WorldviewHighlights = FormWorldviewHighlights;
            data.ProtagonistDesign = FormProtagonistDesign;
            data.SupportingRoles = FormSupportingRoles;
            data.CharacterRelations = FormCharacterRelations;
            data.GoldenFingerDesign = FormGoldenFingerDesign;
            data.CharacterHighlights = FormCharacterHighlights;
            data.PlotStructure = FormPlotStructure;
            data.ConflictDesign = FormConflictDesign;
            data.ClimaxArrangement = FormClimaxArrangement;
            data.ForeshadowingTechnique = FormForeshadowingTechnique;
            data.PlotHighlights = FormPlotHighlights;
        }

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
        {
            try
            {
                var targetCategory = _currentEditingCategory;
                var targetData = _currentEditingData;
                if (_ is TreeNodeItem node)
                {
                    if (node.Tag is BookAnalysisCategory category)
                    {
                        targetCategory = category;
                        targetData = null;
                    }
                    else if (node.Tag is BookAnalysisData data)
                    {
                        targetData = data;
                        targetCategory = null;
                    }
                }

                if (targetCategory != null)
                {
                    var allCategoriesToDelete = CollectCategoryAndChildrenNames(targetCategory.Name);

                    if (allCategoriesToDelete.Any(name => Service.IsCategoryBuiltIn(name)))
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除（含联动删除）。");
                        return;
                    }

                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除分类『{targetCategory.Name}』吗？\n\n注意：该分类及其 {allCategoriesToDelete.Count - 1} 个子分类下的所有书籍分析也会被删除！",
                        "确认删除");
                    if (!result) return;

                    int totalDataDeleted = 0;

                    int totalCategoryDeleted = 0;
                    var categoryIdLookup = Service.GetAllCategories()
                        .ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
                    foreach (var categoryName in allCategoriesToDelete)
                    {
                        categoryIdLookup.TryGetValue(categoryName, out var cId);
                        var dataInCategory = Service.GetAllAnalysis()
                            .Where(a =>
                                (!string.IsNullOrWhiteSpace(cId) && a.CategoryId == cId) ||
                                (string.IsNullOrWhiteSpace(a.CategoryId) && a.Category == categoryName))
                            .ToList();

                        foreach (var analysis in dataInCategory)
                        {
                            _crawlerService.DeleteCrawledContent(analysis.Id);
                            Service.DeleteAnalysis(analysis.Id);
                            totalDataDeleted++;
                        }

                        Service.DeleteCategory(categoryName);
                        if (!Service.IsCategoryBuiltIn(categoryName))
                        {
                            totalCategoryDeleted++;
                        }
                    }

                    if (totalCategoryDeleted == 0)
                    {
                        GlobalToast.Warning("禁止删除", "系统内置分类不可删除。");
                        return;
                    }

                    GlobalToast.Success("删除成功",
                        $"已删除 {totalCategoryDeleted} 个分类及其 {totalDataDeleted} 个书籍分析");

                    _currentEditingCategory = null;
                    ResetForm();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    RefreshTreeAndCategorySelection();
                }
                else if (targetData != null)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"确定要删除书籍分析『{targetData.Name}』吗？",
                        "确认删除");
                    if (!result) return;

                    _crawlerService.DeleteCrawledContent(targetData.Id);
                    Service.DeleteAnalysis(targetData.Id);
                    GlobalToast.Success("删除成功", $"书籍分析『{targetData.Name}』已删除");

                    _currentEditingData = null;
                    ResetForm();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    RefreshTreeAndCategorySelection();
                }
                else
                {
                    GlobalToast.Warning("删除失败", "请先选择要删除的分类或书籍分析");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
            }
        });

        private TM.Framework.Common.ViewModels.AIGenerationConfig? _cachedConfig;
        protected override TM.Framework.Common.ViewModels.AIGenerationConfig? GetAIGenerationConfig()
        {
            return _cachedConfig ??= new TM.Framework.Common.ViewModels.AIGenerationConfig
            {
                Category = "拆书分析师",
                ServiceType = TM.Framework.Common.ViewModels.AIServiceType.ChatEngine,
                ResponseFormat = TM.Framework.Common.ViewModels.ResponseFormat.Json,
                MessagePrefix = "分析书籍",
                ProgressMessage = "正在分析书籍内容，请稍候...",
                CompleteMessage = "AI已生成拆书分析结果，请查看并编辑",
                InputVariables = new()
                {
                    ["书名"] = () => FormName,
                    ["作者"] = () => SourceAuthor,
                    ["类型"] = () => SourceGenre,
                },
                OutputFields = new()
                {
                    ["世界构建手法"] = v => FormWorldBuildingMethod = v,
                    ["力量体系设计"] = v => FormPowerSystemDesign = v,
                    ["环境描写技巧"] = v => FormEnvironmentDescription = v,
                    ["势力设计技巧"] = v => FormFactionDesign = v,
                    ["世界观亮点"] = v => FormWorldviewHighlights = v,
                    ["主角塑造手法"] = v => FormProtagonistDesign = v,
                    ["配角设计技巧"] = v => FormSupportingRoles = v,
                    ["人物关系设计"] = v => FormCharacterRelations = v,
                    ["金手指设计"] = v => FormGoldenFingerDesign = v,
                    ["角色塑造亮点"] = v => FormCharacterHighlights = v,
                    ["情节结构技巧"] = v => FormPlotStructure = v,
                    ["冲突设计手法"] = v => FormConflictDesign = v,
                    ["高潮布局技巧"] = v => FormClimaxArrangement = v,
                    ["伏笔技巧"] = v => FormForeshadowingTechnique = v,
                    ["剧情设计亮点"] = v => FormPlotHighlights = v,
                },
                OutputFieldGetters = new()
                {
                    ["世界构建手法"] = () => FormWorldBuildingMethod,
                    ["力量体系设计"] = () => FormPowerSystemDesign,
                    ["环境描写技巧"] = () => FormEnvironmentDescription,
                    ["势力设计技巧"] = () => FormFactionDesign,
                    ["世界观亮点"] = () => FormWorldviewHighlights,
                    ["主角塑造手法"] = () => FormProtagonistDesign,
                    ["配角设计技巧"] = () => FormSupportingRoles,
                    ["人物关系设计"] = () => FormCharacterRelations,
                    ["金手指设计"] = () => FormGoldenFingerDesign,
                    ["角色塑造亮点"] = () => FormCharacterHighlights,
                    ["情节结构技巧"] = () => FormPlotStructure,
                    ["冲突设计手法"] = () => FormConflictDesign,
                    ["高潮布局技巧"] = () => FormClimaxArrangement,
                    ["伏笔技巧"] = () => FormForeshadowingTechnique,
                    ["剧情设计亮点"] = () => FormPlotHighlights,
                },
                FieldAliases = new()
                {
                    ["世界构建手法"] = new[] { "世界观构建", "世界设定" },
                },
                EnableKeywordExtract = false,
                ContextProvider = async () =>
                {
                    if (_currentEditingData == null) return string.Empty;
                    var excerpt = await _crawlerService.LoadCrawledExcerptAsync(
                        _currentEditingData.Id,
                        maxChapters: 10,
                        maxCharsPerChapter: int.MaxValue,
                        maxTotalChars: int.MaxValue);
                    if (!string.IsNullOrWhiteSpace(excerpt))
                    {
                        return $"<book_excerpt source=\"crawled\" type=\"essence\">\n{excerpt}\n</book_excerpt>";
                    }
                    return string.Empty;
                }
            };
        }

        protected override bool CanExecuteAIGenerate() => _currentEditingData != null;

        protected override bool SupportsBatch(TreeNodeItem categoryNode) => false;
    }
}
