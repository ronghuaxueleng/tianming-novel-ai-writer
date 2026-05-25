using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.SmartParsing.BookAnalysis
{
    public partial class BookAnalysisViewModel
    {
        private async Task ImportLocalFileAsync()
        {
            string? filePath = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择本地小说文件",
                    Filter = "所有支持格式|*.txt;*.md;*.json;*.docx|文本文件 (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json|Word文档 (*.docx)|*.docx|所有文件 (*.*)|*.*",
                    FilterIndex = 1
                };
                if (dialog.ShowDialog() == true)
                    filePath = dialog.FileName;
            });

            if (string.IsNullOrEmpty(filePath)) return;

            var dataName = System.IO.Path.GetFileNameWithoutExtension(filePath).Trim();

            try
            {
                var existing = Service.GetAllAnalysis()
                    .FirstOrDefault(d => string.Equals(d.Name, dataName, StringComparison.Ordinal));

                if (existing != null)
                {
                    _currentEditingData = existing;
                    _currentEditingCategory = null;
                    LoadDataToForm(existing);
                    EnterEditMode();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    ApplyCategorySelection(existing.Category);
                    FocusOnDataItem(existing);
                }
                else
                {
                    var targetCategoryName = _currentEditingCategory?.Name
                        ?? _currentEditingData?.Category;

                    if (string.IsNullOrWhiteSpace(targetCategoryName))
                    {
                        GlobalToast.Info("本地导入", $"文件：{dataName}\n请先在左侧选中一个分类，再点击「本地导入」即可自动创建");
                        return;
                    }

                    var confirm = StandardDialog.ShowConfirm(
                        $"已选择文件：{dataName}\n\n是否在『{targetCategoryName}』中自动创建同名书籍分析数据？\n（导入的内容将归档到该数据项下）",
                        "创建数据确认");

                    if (!confirm) return;

                    var data = new TM.Services.Modules.ProjectData.Models.Design.SmartParsing.BookAnalysisData
                    {
                        Id = TM.Framework.Common.Helpers.Id.ShortIdGenerator.New("D"),
                        Name = dataName,
                        Category = targetCategoryName,
                        Icon = "Icon.Book",
                        IsEnabled = true,
                        SourceBookTitle = dataName,
                        SourceSite = "本地导入"
                    };

                    Service.AddAnalysis(data);
                    RefreshTreeAndCategorySelection();
                    ApplyCategorySelection(data.Category);

                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    EnterEditMode();
                    _getEssenceChaptersCommand?.RaiseCanExecuteChanged();
                    FocusOnDataItem(data);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 本地导入自动建项失败: {ex.Message}");
                GlobalToast.Error("建项失败", $"建项失败：{ex.Message}");
                return;
            }

            try
            {
                CrawlStatus = "正在读取文件...";
                var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                string rawText;

                if (ext == ".docx")
                    rawText = ExtractTextFromDocx(filePath);
                else
                    rawText = await ReadTextFileWithEncodingDetectionAsync(filePath);

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    GlobalToast.Warning("提示", "文件内容为空");
                    CrawlStatus = "导入失败";
                    return;
                }

                var chapters = (ext == ".json")
                    ? ParseJsonToChapters(rawText)
                    : ParseLocalTextToChapters(rawText);

                var crawled = new Models.CrawledContent
                {
                    BookId = _currentEditingData!.Id,
                    BookTitle = string.IsNullOrWhiteSpace(SourceBookTitle) ? dataName : SourceBookTitle,
                    Author = SourceAuthor,
                    SourceUrl = filePath,
                    SourceSite = "本地导入",
                    CrawledAt = DateTime.Now,
                    Chapters = chapters,
                    TotalChapters = chapters.Count,
                    TotalWords = chapters.Sum(c => c.WordCount)
                };

                CrawlStatus = "正在保存...";
                await SaveCrawledContentAsync(_currentEditingData.Id, crawled);

                if (string.IsNullOrWhiteSpace(SourceBookTitle))
                    SourceBookTitle = dataName;
                SourceSite = "本地导入";
                ChapterCount = crawled.TotalChapters;
                TotalWordCount = crawled.TotalWords;
                CrawledAt = crawled.CrawledAt;

                if (_currentEditingData != null)
                {
                    UpdateDataFromForm(_currentEditingData);
                    Service.UpdateAnalysis(_currentEditingData);
                }

                DisplayCrawledPreviewFromContent(crawled);

                CrawlStatus = $"已导入 {crawled.TotalChapters} 章";
                GlobalToast.Success("导入成功", $"已导入 {crawled.TotalChapters} 个章节，共 {crawled.TotalWords} 字");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 本地导入失败: {ex.Message}");
                GlobalToast.Error("导入失败", $"导入失败：{ex.Message}");
                CrawlStatus = "导入失败";
            }
        }

        private static List<Models.CrawledChapter> ParseLocalTextToChapters(string text)
        {
            var matches = _chapterPattern.Matches(text);

            if (matches.Count == 0)
            {
                var wordCount = text.Replace("\r", "").Replace("\n", "").Replace(" ", "").Length;
                return new List<Models.CrawledChapter>
                {
                    new Models.CrawledChapter
                    {
                        Index = 1,
                        Title = "正文",
                        Content = text.Trim(),
                        WordCount = wordCount
                    }
                };
            }

            var result = new List<Models.CrawledChapter>();
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var title = match.Value.Trim();
                var contentStart = match.Index + match.Length;
                var contentEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : text.Length;
                var content = text.Substring(contentStart, contentEnd - contentStart).Trim();
                var wordCount = content.Replace("\r", "").Replace("\n", "").Replace(" ", "").Length;

                result.Add(new Models.CrawledChapter
                {
                    Index = i + 1,
                    Title = title,
                    Content = content,
                    WordCount = wordCount
                });
            }
            return result;
        }

        private static async Task<string> ReadTextFileWithEncodingDetectionAsync(string filePath)
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            if (bytes.Length == 0) return string.Empty;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            var utf8Text = System.Text.Encoding.UTF8.GetString(bytes);
            if (!utf8Text.Contains('\uFFFD'))
                return utf8Text;

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                var gbkEncoding = System.Text.Encoding.GetEncoding("gb2312");
                return gbkEncoding.GetString(bytes);
            }
            catch
            {
                return utf8Text;
            }
        }

        private static List<Models.CrawledChapter> ParseJsonToChapters(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                System.Text.Json.JsonElement chaptersArray;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    chaptersArray = root;
                }
                else if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                         && root.TryGetProperty("chapters", out var chArr)
                         && chArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    chaptersArray = chArr;
                }
                else
                {
                    return ParseLocalTextToChapters(json);
                }

                var result = new List<Models.CrawledChapter>();
                int index = 1;
                foreach (var item in chaptersArray.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? $"第{index}章"
                              : item.TryGetProperty("Title", out var t2) ? t2.GetString() ?? $"第{index}章"
                              : $"第{index}章";
                    var content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty
                                : item.TryGetProperty("Content", out var c2) ? c2.GetString() ?? string.Empty
                                : string.Empty;
                    var wordCount = content.Replace("\r", "").Replace("\n", "").Replace(" ", "").Length;

                    result.Add(new Models.CrawledChapter
                    {
                        Index = index,
                        Title = title.Trim(),
                        Content = content.Trim(),
                        WordCount = wordCount
                    });
                    index++;
                }
                return result.Count > 0 ? result : ParseLocalTextToChapters(json);
            }
            catch
            {
                return ParseLocalTextToChapters(json);
            }
        }

        private static string ExtractTextFromDocx(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                var wNs = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");

                using (var zip = System.IO.Compression.ZipFile.OpenRead(filePath))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) return string.Empty;

                    using var stream = entry.Open();
                    var doc = System.Xml.Linq.XDocument.Load(stream);

                    foreach (var paragraph in doc.Descendants(wNs + "p"))
                    {
                        var texts = paragraph.Descendants(wNs + "t").Select(t => t.Value);
                        var line = string.Join("", texts);
                        if (!string.IsNullOrEmpty(line))
                            sb.AppendLine(line);
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] DOCX 解析失败: {ex.Message}");
                return string.Empty;
            }
        }

        private static readonly string _urlHistoryPath = StoragePathHelper.GetFilePath(
            "Modules",
            "Design/SmartParsing/BookAnalysis",
            "url_history.json");

        private static readonly string[] DefaultNovelSites =
        {
            "http://www.shuquta.com/",
            "http://www.xheiyan.info/",
            "https://m.bqgde.de/",
        };

        public System.Collections.ObjectModel.ObservableCollection<string> UrlHistory { get; } = new();

        public event Action<string>? NavigateRequested;
        public event Action? GoBackRequested;

        private string? _selectedHistoryUrl;
        public string? SelectedHistoryUrl
        {
            get => _selectedHistoryUrl;
            set
            {
                _selectedHistoryUrl = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    CurrentUrl = value;
                    AddUrlToHistory(value);
                    IsWebViewVisible = true;
                    OnPropertyChanged(nameof(IsWebViewVisible));
                    NavigateRequested?.Invoke(value);
                }
            }
        }

        private ICommand? _goBackCommand;
        public ICommand GoBackCommand => _goBackCommand ??= new RelayCommand(_ => GoBackRequested?.Invoke());

        private ICommand? _clearUrlHistoryCommand;
        public ICommand ClearUrlHistoryCommand => _clearUrlHistoryCommand ??= new RelayCommand(_ =>
        {
            var toRemove = UrlHistory.Where(u => !DefaultNovelSites.Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
            if (toRemove.Count == 0)
            {
                GlobalToast.Info("提示", "历史记录为空，无需清除");
                return;
            }
            if (!StandardDialog.ShowConfirm($"确定要清除 {toRemove.Count} 条历史记录吗？\n（内置的3个默认网站不会被清除）", "清除历史记录"))
                return;
            foreach (var url in toRemove)
                UrlHistory.Remove(url);
            SaveUrlHistory().SafeFireAndForget(ex => TM.App.Log($"[BookAnalysisViewModel] {ex.Message}"));
            GlobalToast.Success("已清除", $"已清除 {toRemove.Count} 条历史记录");
        });

        private ICommand? _navigateCommand;
        public ICommand NavigateCommand => _navigateCommand ??= new RelayCommand(_ =>
        {
            var url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            AddUrlToHistory(url);
            IsWebViewVisible = true;
            OnPropertyChanged(nameof(IsWebViewVisible));
            NavigateRequested?.Invoke(url);
        });

        private ICommand? _deleteUrlCommand;
        public ICommand DeleteUrlCommand => _deleteUrlCommand ??= new RelayCommand(param =>
        {
            if (param is string url && UrlHistory.Contains(url))
            {
                UrlHistory.Remove(url);
                SaveUrlHistory().SafeFireAndForget(ex => TM.App.Log($"[BookAnalysisViewModel] {ex.Message}"));
            }
        });

        private void LoadUrlHistory()
        {
            AsyncSettingsLoader.RunOrDeferAsync(async () =>
            {
                List<string> urls = new();
                string? error = null;

                try
                {
                    if (System.IO.File.Exists(_urlHistoryPath))
                    {
                        var json = await System.IO.File.ReadAllTextAsync(_urlHistoryPath).ConfigureAwait(false);
                        urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                return () =>
                {
                    try
                    {
                        UrlHistory.Clear();

                        if (urls.Count > 0)
                        {
                            foreach (var url in urls)
                            {
                                UrlHistory.Add(url);
                            }
                            TM.App.Log($"[BookAnalysisViewModel] 已加载 {urls.Count} 条URL历史记录");
                        }

                        foreach (var url in DefaultNovelSites)
                        {
                            if (!UrlHistory.Contains(url))
                                UrlHistory.Add(url);
                        }

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            TM.App.Log($"[BookAnalysisViewModel] 加载URL历史记录失败: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[BookAnalysisViewModel] 加载URL历史记录失败: {ex.Message}");
                    }
                };
            }, "BookAnalysis.UrlHistory");
        }

        private async Task SaveUrlHistory()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_urlHistoryPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                var tmpBav = _urlHistoryPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = System.IO.File.Create(tmpBav))
                {
                    await System.Text.Json.JsonSerializer.SerializeAsync(stream, UrlHistory.ToList(), IndentedJsonOptions);
                }
                System.IO.File.Move(tmpBav, _urlHistoryPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 保存URL历史失败: {ex.Message}");
            }
        }

        private void AddUrlToHistory(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (UrlHistory.Contains(url))
            {
                UrlHistory.Remove(url);
            }

            UrlHistory.Insert(0, url);

            while (UrlHistory.Count > 30)
            {
                UrlHistory.RemoveAt(UrlHistory.Count - 1);
            }

            SaveUrlHistory().SafeFireAndForget(ex => TM.App.Log($"[BookAnalysisViewModel] {ex.Message}"));
        }

        private RangeObservableCollection<Crawler.ChapterContent> _chapterList = new();
        private Crawler.ChapterContent? _selectedChapter;
        private string _selectedChapterContent = string.Empty;

        public RangeObservableCollection<Crawler.ChapterContent> ChapterList { get => _chapterList; set { _chapterList = value; OnPropertyChanged(); } }
        public Crawler.ChapterContent? SelectedChapter
        {
            get => _selectedChapter;
            set
            {
                _selectedChapter = value;
                OnPropertyChanged();
                _ = LoadSelectedChapterContentAsync();
            }
        }
        public string SelectedChapterContent { get => _selectedChapterContent; set { _selectedChapterContent = value; OnPropertyChanged(); } }

        private async Task LoadSelectedChapterContentAsync()
        {
            if (_currentEditingData == null || SelectedChapter == null)
            {
                SelectedChapterContent = string.Empty;
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(SelectedChapter.Content))
                {
                    SelectedChapterContent = SelectedChapter.Content;
                    return;
                }

                if (string.IsNullOrWhiteSpace(SelectedChapter.FileName))
                {
                    SelectedChapterContent = string.Empty;
                    return;
                }

                SelectedChapterContent = string.Empty;
                var text = await _crawlerService.LoadChapterContentAsync(_currentEditingData.Id, SelectedChapter.FileName);
                SelectedChapter.Content = text;
                SelectedChapterContent = text;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BookAnalysisViewModel] 加载章节内容失败: {ex.Message}");
                SelectedChapterContent = string.Empty;
            }
        }

        private string _formWorldBuildingMethod = string.Empty;
        private string _formPowerSystemDesign = string.Empty;
        private string _formEnvironmentDescription = string.Empty;
        private string _formFactionDesign = string.Empty;
        private string _formWorldviewHighlights = string.Empty;

        public string FormWorldBuildingMethod { get => _formWorldBuildingMethod; set { _formWorldBuildingMethod = value; OnPropertyChanged(); } }
        public string FormPowerSystemDesign { get => _formPowerSystemDesign; set { _formPowerSystemDesign = value; OnPropertyChanged(); } }
        public string FormEnvironmentDescription { get => _formEnvironmentDescription; set { _formEnvironmentDescription = value; OnPropertyChanged(); } }
        public string FormFactionDesign { get => _formFactionDesign; set { _formFactionDesign = value; OnPropertyChanged(); } }
        public string FormWorldviewHighlights { get => _formWorldviewHighlights; set { _formWorldviewHighlights = value; OnPropertyChanged(); } }

        private string _formProtagonistDesign = string.Empty;
        private string _formSupportingRoles = string.Empty;
        private string _formCharacterRelations = string.Empty;
        private string _formGoldenFingerDesign = string.Empty;
        private string _formCharacterHighlights = string.Empty;

        public string FormProtagonistDesign { get => _formProtagonistDesign; set { _formProtagonistDesign = value; OnPropertyChanged(); } }
        public string FormSupportingRoles { get => _formSupportingRoles; set { _formSupportingRoles = value; OnPropertyChanged(); } }
        public string FormCharacterRelations { get => _formCharacterRelations; set { _formCharacterRelations = value; OnPropertyChanged(); } }
        public string FormGoldenFingerDesign { get => _formGoldenFingerDesign; set { _formGoldenFingerDesign = value; OnPropertyChanged(); } }
        public string FormCharacterHighlights { get => _formCharacterHighlights; set { _formCharacterHighlights = value; OnPropertyChanged(); } }

        private string _formPlotStructure = string.Empty;
        private string _formConflictDesign = string.Empty;
        private string _formClimaxArrangement = string.Empty;
        private string _formForeshadowingTechnique = string.Empty;
        private string _formPlotHighlights = string.Empty;

        public string FormPlotStructure { get => _formPlotStructure; set { _formPlotStructure = value; OnPropertyChanged(); } }
        public string FormConflictDesign { get => _formConflictDesign; set { _formConflictDesign = value; OnPropertyChanged(); } }
        public string FormClimaxArrangement { get => _formClimaxArrangement; set { _formClimaxArrangement = value; OnPropertyChanged(); } }
        public string FormForeshadowingTechnique { get => _formForeshadowingTechnique; set { _formForeshadowingTechnique = value; OnPropertyChanged(); } }
        public string FormPlotHighlights { get => _formPlotHighlights; set { _formPlotHighlights = value; OnPropertyChanged(); } }

    }
}

