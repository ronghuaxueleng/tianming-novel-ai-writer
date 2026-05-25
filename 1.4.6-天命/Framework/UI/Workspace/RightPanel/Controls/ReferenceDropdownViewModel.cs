using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using System.IO;
using System.Text.Json;

using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Framework.AI.QueryRouting;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ReferenceDropdownViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<string>? ReferenceSelected;

        private readonly ContextService _contextService;
        private readonly GeneratedContentService _genContentService;
        private readonly QueryRoutingService _routingService;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private bool _isOpen;
        private string _searchText = string.Empty;
        private bool _showTypes = true;
        private bool _showItems;
        private string _selectedType = string.Empty;
        private UIElement? _placementTarget;
        private readonly List<ReferenceItem> _allItems = new();

        public ReferenceDropdownViewModel(
            ContextService contextService,
            GeneratedContentService genContentService,
            QueryRoutingService routingService)
        {
            _contextService = contextService;
            _genContentService = genContentService;
            _routingService = routingService;
            ReferenceTypes = new ObservableCollection<ReferenceTypeInfo>(
                ReferenceParser.GetAvailableTypes());

            FilteredItems = new ObservableCollection<ReferenceItem>();

            SelectTypeCommand = new RelayCommand(p => SelectType(p as ReferenceTypeInfo));
            SelectItemCommand = new RelayCommand(p => SelectItem(p as ReferenceItem));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen != value)
                {
                    _isOpen = value;
                    OnPropertyChanged();

                    if (value)
                    {
                        Reset();
                    }
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    FilterItems();
                }
            }
        }

        public bool ShowTypes
        {
            get => _showTypes;
            set
            {
                if (_showTypes != value)
                {
                    _showTypes = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowItems
        {
            get => _showItems;
            set
            {
                if (_showItems != value)
                {
                    _showItems = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEmpty => ShowItems && FilteredItems.Count == 0;

        public UIElement? PlacementTarget
        {
            get => _placementTarget;
            set
            {
                if (_placementTarget != value)
                {
                    _placementTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<ReferenceTypeInfo> ReferenceTypes { get; }

        public ObservableCollection<ReferenceItem> FilteredItems { get; }

        public ICommand SelectTypeCommand { get; }

        public ICommand SelectItemCommand { get; }

        #endregion

        #region 方法

        public void Show(UIElement target)
        {
            PlacementTarget = target;
            IsOpen = true;
        }

        public void Hide()
        {
            IsOpen = false;
        }

        private void Reset()
        {
            SearchText = string.Empty;
            ShowTypes = true;
            ShowItems = false;
            _selectedType = string.Empty;
            _allItems.Clear();
            FilteredItems.Clear();
        }

        private void SelectType(ReferenceTypeInfo? type)
        {
            if (type == null) return;

            _selectedType = type.Type;
            ShowTypes = false;
            ShowItems = true;

            LoadItemsForType(type.Type);
        }

        private void SelectItem(ReferenceItem? item)
        {
            if (item == null) return;

            var reference = _selectedType == "仿写"
                ? $"@{_selectedType}:{item.Name}"
                : $"@{_selectedType}:{item.Id}";
            ReferenceSelected?.Invoke(reference);
            Hide();
        }

        private void LoadItemsForType(string type)
        {
            _allItems.Clear();
            FilteredItems.Clear();

            _ = LoadItemsForTypeAsync(type);
        }

        private async Task LoadItemsForTypeAsync(string type)
        {
            try
            {
                var contextService = _contextService;

                switch (type)
                {
                    case "角色":
                        if (!await TryLoadItemsFromRoutingAsync("characters"))
                        {
                            var charContext = await contextService.GetCharacterContextAsync();
                            if (charContext?.CharacterRules != null)
                            {
                                foreach (var profile in charContext.CharacterRules)
                                {
                                    _allItems.Add(new ReferenceItem
                                    {
                                        Id = profile.Id ?? profile.Name ?? string.Empty,
                                        Name = profile.Name ?? "未命名角色"
                                    });
                                }
                            }
                        }
                        break;

                    case "世界观":
                        if (!await TryLoadItemsFromRoutingAsync("worldrules"))
                        {
                            var worldContext = await contextService.GetWorldviewContextAsync();
                            if (worldContext?.WorldRules != null)
                            {
                                foreach (var rule in worldContext.WorldRules)
                                {
                                    _allItems.Add(new ReferenceItem
                                    {
                                        Id = rule.Id ?? string.Empty,
                                        Name = rule.Name ?? "未命名规则"
                                    });
                                }
                            }
                        }
                        break;

                    case "续写":
                    case "重写":
                        var genContent = _genContentService;
                        var chapters = await genContent.GetGeneratedChaptersAsync();
                        if (chapters != null)
                        {
                            foreach (var ch in chapters
                                         .OrderByDescending(c => c.VolumeNumber)
                                         .ThenByDescending(c => c.ChapterNumber))
                            {
                                _allItems.Add(new ReferenceItem
                                {
                                    Id = ch.Id ?? string.Empty,
                                    Name = ch.Title ?? "未命名章节"
                                });
                            }
                        }
                        break;

                    case "仿写":
                        await LoadCrawledBookInfosAsync();
                        break;

                    case "大纲":
                        var outline = await contextService.GetOutlineContextAsync();
                        if (outline?.Outlines != null)
                        {
                            foreach (var item in outline.Outlines)
                            {
                                _allItems.Add(new ReferenceItem
                                {
                                    Id = item.Id,
                                    Name = item.Name
                                });
                            }
                        }
                        break;
                }

                if (_allItems.Count == 0)
                {
                    FilteredItems.Clear();
                    FilteredItems.Add(new ReferenceItem { Id = string.Empty, Name = "（暂无数据）" });
                }
                else
                {
                    FilterItems();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ReferenceDropdown] 加载数据失败: {ex.Message}");
                _allItems.Clear();
                FilteredItems.Add(new ReferenceItem { Id = "", Name = "（加载失败）" });
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        private async Task LoadCrawledBookInfosAsync()
        {
            var entries = new List<(DateTime SortTime, string BookId, string Title)>();

            try
            {
                entries = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = new List<(DateTime SortTime, string BookId, string Title)>();
                    var crawledBasePath = StoragePathHelper.GetModulesStoragePath("Design/SmartParsing/BookAnalysis/CrawledBooks");
                    if (!Directory.Exists(crawledBasePath))
                        return result;

                    foreach (var bookDir in Directory.GetDirectories(crawledBasePath))
                    {
                        var bookId = Path.GetFileName(bookDir);
                        if (string.IsNullOrWhiteSpace(bookId))
                            continue;

                        var bookInfoPath = Path.Combine(bookDir, "book_info.json");
                        if (!File.Exists(bookInfoPath))
                            continue;

                        var json = File.ReadAllText(bookInfoPath);
                        var title = TryReadBookTitle(json);
                        if (string.IsNullOrWhiteSpace(title))
                            title = bookId;

                        var sortTime = File.GetLastWriteTime(bookInfoPath);
                        result.Add((sortTime, bookId, title));
                    }

                    return result;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ReferenceDropdown] 读取爬取书名失败: {ex.Message}");
            }

            foreach (var entry in entries.OrderByDescending(e => e.SortTime))
            {
                _allItems.Add(new ReferenceItem
                {
                    Id = entry.BookId,
                    Name = entry.Title
                });
            }
        }

        private static string? TryReadBookTitle(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("title", out var titleProp))
                {
                    return titleProp.GetString();
                }

                if (doc.RootElement.TryGetProperty("Title", out var titleProp2))
                {
                    return titleProp2.GetString();
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private async Task<bool> TryLoadItemsFromRoutingAsync(string category)
        {
            var json = await _routingService.ListAvailableIdsAsync(category);
            if (string.IsNullOrWhiteSpace(json) || json.StartsWith("[未找到]", StringComparison.Ordinal))
                return false;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = GetString(item, "Id") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var display = GetString(item, "Display")
                              ?? GetString(item, "Name")
                              ?? id;

                _allItems.Add(new ReferenceItem
                {
                    Id = id,
                    Name = display
                });
            }

            return _allItems.Count > 0;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }

        private void FilterItems()
        {
            if (!ShowItems)
            {
                OnPropertyChanged(nameof(IsEmpty));
                return;
            }

            if (_allItems.Count == 0)
            {
                FilteredItems.Clear();
                OnPropertyChanged(nameof(IsEmpty));
                return;
            }

            FilteredItems.Clear();

            var keyword = SearchText?.Trim();

            if (string.IsNullOrEmpty(keyword))
            {
                foreach (var item in _allItems)
                {
                    FilteredItems.Add(item);
                }
            }
            else
            {
                foreach (var item in _allItems)
                {
                    if (IsMatch(item, keyword))
                    {
                        FilteredItems.Add(item);
                    }
                }
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        private static bool IsMatch(ReferenceItem item, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return true;

            return (!string.IsNullOrEmpty(item.Name) &&
                    item.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0)
                   || (!string.IsNullOrEmpty(item.Id) &&
                       item.Id.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        #endregion
    }

    public class ReferenceItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
