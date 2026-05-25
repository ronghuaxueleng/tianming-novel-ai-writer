using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.MVVM;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Models.Generate.Content;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Modules.Generate.Content.ChapterPreview
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterPreviewViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private string _searchKeyword = string.Empty;
        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                if (_searchKeyword != value)
                {
                    _searchKeyword = value;
                    OnPropertyChanged();
                    FilterChapters();
                }
            }
        }

        public ObservableCollection<VolumeTreeItem> VolumeTree { get; } = new();
        private ContentGuide? _contentGuide;
        private readonly VolumeDesignService _volumeDesignService;

        private bool _contentLoaded;

        private Dictionary<string, string> _characterNameMap = new();
        private Dictionary<string, string> _locationNameMap = new();
        private Dictionary<string, string> _factionNameMap = new();
        private Dictionary<string, string> _plotRuleNameMap = new();

        private ContentGuideEntry? _selectedChapterDetail;
        public ContentGuideEntry? SelectedChapterDetail
        {
            get => _selectedChapterDetail;
            set
            {
                _selectedChapterDetail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedChapter));
                OnPropertyChanged(nameof(CharacterNames));
                OnPropertyChanged(nameof(LocationNames));
                OnPropertyChanged(nameof(FactionNames));
                OnPropertyChanged(nameof(PlotRuleNames));
            }
        }

        public bool HasSelectedChapter => SelectedChapterDetail != null;

        public ObservableCollection<string> CharacterNames => new(
            _selectedChapterDetail?.ContextIds?.Characters?
                .Select(id => _characterNameMap.TryGetValue(id, out var name) ? name : id)
                .ToList() ?? new List<string>());

        public ObservableCollection<string> LocationNames => new(
            _selectedChapterDetail?.ContextIds?.Locations?
                .Select(id => _locationNameMap.TryGetValue(id, out var name) ? name : id)
                .ToList() ?? new List<string>());

        public ObservableCollection<string> FactionNames => new(
            _selectedChapterDetail?.ContextIds?.Factions?
                .Select(id => _factionNameMap.TryGetValue(id, out var name) ? name : id)
                .ToList() ?? new List<string>());

        public ObservableCollection<string> PlotRuleNames => new(
            _selectedChapterDetail?.ContextIds?.PlotRules?
                .Select(id => _plotRuleNameMap.TryGetValue(id, out var name) ? name : id)
                .ToList() ?? new List<string>());

        private int _totalChapters;
        public int TotalChapters
        {
            get => _totalChapters;
            set { _totalChapters = value; OnPropertyChanged(); }
        }

        private int _totalVolumes;
        public int TotalVolumes
        {
            get => _totalVolumes;
            set { _totalVolumes = value; OnPropertyChanged(); }
        }

        private ICommand? _selectChapterCommand;
        public ICommand SelectChapterCommand => _selectChapterCommand ??= new RelayCommand(param =>
        {
            if (param is ChapterTreeItem chapter)
            {
                LoadChapterDetail(chapter.ChapterId);
            }
        });

        private ICommand? _refreshCommand;
        public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(_ =>
        {
            _ = LoadContentGuideAsync(forceReload: true);
        });

        public ChapterPreviewViewModel(VolumeDesignService volumeDesignService)
        {
            _volumeDesignService = volumeDesignService;

            TM.Services.Modules.ProjectData.Implementations.GuideContextService.CacheInvalidated += (_, _) => _contentLoaded = false;

            _ = LoadContentGuideAsync();
        }

        private async Task LoadContentGuideAsync(bool forceReload = false)
        {
            try
            {
                await _volumeDesignService.InitializeAsync();

                var guideService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideContextService>();
                _contentGuide = await guideService.GetContentGuideAsync();

                if (_contentGuide?.Chapters == null || _contentGuide.Chapters.Count == 0)
                {
                    ClearVolumeTree();
                    _contentLoaded = false;
                    if (forceReload)
                        GlobalToast.Warning("提示", "请先在数据中心执行打包操作");
                    return;
                }

                if (forceReload || !_contentLoaded)
                    await LoadNameMappingsAsync();

                BuildVolumeTree();
                _contentLoaded = true;
                TM.App.Log($"[ChapterPreviewViewModel] 加载成功: {TotalChapters}章, {TotalVolumes}卷");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterPreviewViewModel] 加载失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        }

        private async Task LoadNameMappingsAsync()
        {
            try
            {
                _characterNameMap.Clear();
                _locationNameMap.Clear();
                _factionNameMap.Clear();
                _plotRuleNameMap.Clear();

                var elementsPath = Path.Combine(
                    StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");

                if (File.Exists(elementsPath))
                {
                    var json = await File.ReadAllTextAsync(elementsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("characterrules", out var charModule) &&
                            charModule.TryGetProperty("character_rules", out var characters))
                        {
                            foreach (var item in characters.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                    _characterNameMap[id] = name;
                            }
                        }

                        if (data.TryGetProperty("locationrules", out var locModule) &&
                            locModule.TryGetProperty("location_rules", out var locations))
                        {
                            foreach (var item in locations.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                    _locationNameMap[id] = name;
                            }
                        }

                        if (data.TryGetProperty("factionrules", out var facModule) &&
                            facModule.TryGetProperty("faction_rules", out var factions))
                        {
                            foreach (var item in factions.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                    _factionNameMap[id] = name;
                            }
                        }

                        if (data.TryGetProperty("plotrules", out var plotModule) &&
                            plotModule.TryGetProperty("plot_rules", out var plotRules))
                        {
                            foreach (var item in plotRules.EnumerateArray())
                            {
                                var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                                var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                    _plotRuleNameMap[id] = name;
                            }
                        }
                    }
                }
                TM.App.Log($"[ChapterPreviewViewModel] 名称映射加载完成: {_characterNameMap.Count}角色, {_locationNameMap.Count}地点, {_factionNameMap.Count}势力, {_plotRuleNameMap.Count}剧情");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterPreviewViewModel] 名称映射加载失败: {ex.Message}");
            }
        }

        private static async Task<List<T>> LoadDataListAsync<T>(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<T>();
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterPreviewViewModel] 读取数据失败 [{filePath}]: {ex.Message}");
                return new List<T>();
            }
        }

        private void BuildVolumeTree(IEnumerable<ContentGuideEntry>? entries = null)
        {
            VolumeTree.Clear();
            if (_contentGuide?.Chapters == null) return;

            var chapterEntries = entries?.ToList() ?? _contentGuide.Chapters.Values.ToList();
            if (chapterEntries.Count == 0)
            {
                TotalChapters = 0;
                TotalVolumes = 0;
                return;
            }

            var volumeDesigns = GetVolumeDesigns();
            var volumeById = volumeDesigns
                .Where(v => !string.IsNullOrWhiteSpace(v.Id))
                .ToDictionary(v => v.Id, StringComparer.OrdinalIgnoreCase);
            var volumeByNumber = volumeDesigns
                .Where(v => v.VolumeNumber > 0)
                .ToDictionary(v => v.VolumeNumber, v => v);

            var grouped = new Dictionary<string, List<ContentGuideEntry>>(StringComparer.OrdinalIgnoreCase);
            const string orphanKey = "__orphan__";

            foreach (var entry in chapterEntries)
            {
                var volume = ResolveVolumeDesign(entry, volumeById, volumeByNumber);
                var key = volume?.Id ?? orphanKey;
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<ContentGuideEntry>();
                    grouped[key] = list;
                }
                list.Add(entry);
            }

            var includeEmptyVolumes = entries == null;
            foreach (var volume in volumeDesigns)
            {
                if (string.IsNullOrWhiteSpace(volume.Id)) continue;
                grouped.TryGetValue(volume.Id, out var chapters);
                if (!includeEmptyVolumes && (chapters == null || chapters.Count == 0))
                    continue;

                AddVolumeTreeItem(volume, chapters ?? new List<ContentGuideEntry>());
            }

            TotalChapters = chapterEntries.Count;
            TotalVolumes = VolumeTree.Count;
        }

        private void AddVolumeTreeItem(VolumeDesignData? volume, IEnumerable<ContentGuideEntry> chapters)
        {
            var volItem = new VolumeTreeItem
            {
                VolumeNumber = volume?.VolumeNumber ?? 0,
                Name = volume == null ? "未归属卷" : BuildVolumeDisplayName(volume),
                Icon = volume == null ? "📦" : "📚",
                IsExpanded = false
            };
            foreach (var ch in chapters.OrderBy(c => ResolveChapterNumber(c)))
            {
                var chapterNumber = ResolveChapterNumber(ch);
                var displayTitle = string.IsNullOrWhiteSpace(ch.Title)
                    ? (chapterNumber > 0 ? $"第{chapterNumber}章" : ch.ChapterId)
                    : (chapterNumber > 0 ? $"第{chapterNumber}章-{ch.Title}" : ch.Title);

                volItem.Chapters.Add(new ChapterTreeItem
                {
                    ChapterId = ch.ChapterId,
                    Title = displayTitle
                });
            }

            VolumeTree.Add(volItem);
        }

        private void LoadChapterDetail(string chapterId)
        {
            if (_contentGuide?.Chapters.TryGetValue(chapterId, out var entry) == true)
            {
                SelectedChapterDetail = entry;
                TM.App.Log($"[ChapterPreviewViewModel] 选中章节: {chapterId}");
            }
        }

        private void FilterChapters()
        {
            if (_contentGuide == null) return;

            if (string.IsNullOrWhiteSpace(SearchKeyword))
            {
                BuildVolumeTree();
                return;
            }

            VolumeTree.Clear();
            var keyword = SearchKeyword.ToLower();

            var filtered = _contentGuide.Chapters.Values
                .Where(c => c.ChapterId.ToLower().Contains(keyword) ||
                           (c.Title?.ToLower().Contains(keyword) ?? false))
                .ToList();

            BuildVolumeTree(filtered);
        }

        private List<VolumeDesignData> GetVolumeDesigns()
        {
            _volumeDesignService.EnsureInitialized();
            return _volumeDesignService.GetAllVolumeDesigns()
                .OrderBy(v => v.VolumeNumber)
                .ToList();
        }

        private static VolumeDesignData? ResolveVolumeDesign(
            ContentGuideEntry entry,
            Dictionary<string, VolumeDesignData> volumeById,
            Dictionary<int, VolumeDesignData> volumeByNumber)
        {
            var volumeDesignId = entry.ContextIds?.VolumeDesignId;
            if (!string.IsNullOrWhiteSpace(volumeDesignId)
                && volumeById.TryGetValue(volumeDesignId, out var byId))
            {
                return byId;
            }

            if (!string.IsNullOrWhiteSpace(entry.Volume))
            {
                foreach (var volume in volumeById.Values)
                {
                    if (entry.Volume.Equals(volume.Name, StringComparison.OrdinalIgnoreCase)
                        || entry.Volume.Equals(volume.VolumeTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        return volume;
                    }

                    if (volume.VolumeNumber > 0 && entry.Volume.Contains($"第{volume.VolumeNumber}卷", StringComparison.OrdinalIgnoreCase))
                    {
                        return volume;
                    }
                }
            }

            var parsedVolumeNumber = ExtractVolumeNumber(entry.ChapterId);
            if (parsedVolumeNumber > 0 && volumeByNumber.TryGetValue(parsedVolumeNumber, out var byNumber))
            {
                return byNumber;
            }

            return null;
        }

        private static string BuildVolumeDisplayName(VolumeDesignData volume)
        {
            if (volume.VolumeNumber > 0)
            {
                var title = string.IsNullOrWhiteSpace(volume.VolumeTitle) ? string.Empty : $" {volume.VolumeTitle}";
                return $"第{volume.VolumeNumber}卷{title}".Trim();
            }

            if (!string.IsNullOrWhiteSpace(volume.VolumeTitle))
                return volume.VolumeTitle;

            return string.IsNullOrWhiteSpace(volume.Name) ? "未命名卷" : volume.Name;
        }

        private static int ResolveChapterNumber(ContentGuideEntry entry)
        {
            if (entry.ChapterNumber > 0) return entry.ChapterNumber;
            return ExtractChapterNumber(entry.ChapterId);
        }

        private void ClearVolumeTree()
        {
            VolumeTree.Clear();
            TotalChapters = 0;
            TotalVolumes = 0;
            SelectedChapterDetail = null;
            _contentGuide = null;
        }

        private static int ExtractVolumeNumber(string chapterId)
        {
            return ChapterParserHelper.ExtractVolumeNumber(chapterId);
        }

        private static int ExtractChapterNumber(string chapterId)
        {
            return ChapterParserHelper.ExtractChapterNumber(chapterId);
        }
    }
}
