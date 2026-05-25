using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    public enum EditorTabContentType
    {
        Chapter,
        Plan,
        Homepage
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class EditorTab : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _content = string.Empty;
        private string _originalContent = string.Empty;
        private bool _isActive;
        private bool _isModified;
        private bool _isNew;
        private EditorTabContentType _contentType = EditorTabContentType.Chapter;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                IsModified = _content != _originalContent;
                OnPropertyChanged();
            }
        }

        public string OriginalContent
        {
            get => _originalContent;
            set { _originalContent = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTitle)); }
        }

        public string DisplayTitle => IsModified ? $"{Title} *" : Title;

        public bool IsHomepage => Id == "homepage";

        public EditorTabContentType ContentType
        {
            get => _contentType;
            set { _contentType = value; OnPropertyChanged(); }
        }

        public bool IsPlanTab => ContentType == EditorTabContentType.Plan;

        public bool IsNew
        {
            get => _isNew;
            set { _isNew = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class EditorTabManager : INotifyPropertyChanged
    {
        private const int MaxTabs = 6;
        private const string HOMEPAGE_ID = "homepage";
        private EditorTab? _activeTab;

        public ObservableCollection<EditorTab> Tabs { get; } = new();

        public event Action? AllTabsClosed;

        public EditorTabManager()
        {
            AddHomepageTab();
        }

        public void AddHomepageTab()
        {
            if (Tabs.Any(t => t.Id == HOMEPAGE_ID)) return;

            var homepage = new EditorTab
            {
                Id = HOMEPAGE_ID,
                Title = "主页",
                Content = string.Empty,
                OriginalContent = string.Empty,
                ContentType = EditorTabContentType.Homepage
            };

            Tabs.Add(homepage);
            ActivateTab(homepage);
            OnPropertyChanged(nameof(HasTabs));
        }

        public bool IsHomepage(EditorTab tab) => tab.Id == HOMEPAGE_ID;

        public EditorTab? ActiveTab
        {
            get => _activeTab;
            private set
            {
                if (_activeTab != value)
                {
                    if (_activeTab != null) _activeTab.IsActive = false;
                    _activeTab = value;
                    if (_activeTab != null) _activeTab.IsActive = true;
                    OnPropertyChanged();
                    ActiveTabChanged?.Invoke(this, _activeTab);
                }
            }
        }

        public bool HasTabs => Tabs.Count > 0;

        public event EventHandler<EditorTab?>? ActiveTabChanged;

        public Func<EditorTab, bool>? TabClosing;

        public bool OpenTab(string id, string title, string content, string originalContent, EditorTabContentType contentType = EditorTabContentType.Chapter)
        {
            var existing = Tabs.FirstOrDefault(t => t.Id == id);
            if (existing != null)
            {
                var shouldUpdateTitle = !string.IsNullOrWhiteSpace(title)
                    && (existing.IsNew
                        || string.IsNullOrWhiteSpace(existing.Title)
                        || existing.Title == existing.Id
                        || existing.Title.StartsWith("新章节", StringComparison.Ordinal));

                if (shouldUpdateTitle)
                {
                    existing.Title = title;
                }

                if (!existing.IsModified)
                {
                    existing.OriginalContent = originalContent;
                    existing.Content = content;
                }

                ActivateTab(existing);
                return true;
            }

            var nonHomepageTabs = Tabs.Count(t => t.Id != HOMEPAGE_ID);
            if (nonHomepageTabs >= MaxTabs)
            {
                var tabToClose = Tabs.FirstOrDefault(t => t.Id != HOMEPAGE_ID && t != ActiveTab);
                if (tabToClose != null)
                {
                    if (tabToClose.IsModified)
                    {
                        tabToClose = Tabs.FirstOrDefault(t => t.Id != HOMEPAGE_ID && t != ActiveTab && !t.IsModified);
                    }

                    if (tabToClose != null)
                    {
                        Tabs.Remove(tabToClose);
                        TM.App.Log($"[EditorTabManager] 标签页已满，自动关闭: {tabToClose.Title}");
                    }
                    else
                    {
                        tabToClose = Tabs.FirstOrDefault(t => t.Id != HOMEPAGE_ID && t != ActiveTab);
                        if (tabToClose != null)
                        {
                            Tabs.Remove(tabToClose);
                            TM.App.Log($"[EditorTabManager] 标签页已满，强制关闭: {tabToClose.Title}");
                        }
                    }
                }
            }

            if (id != HOMEPAGE_ID)
            {
                var homepage = Tabs.FirstOrDefault(t => t.Id == HOMEPAGE_ID);
                if (homepage != null)
                {
                    Tabs.Remove(homepage);
                }
            }

            var tab = new EditorTab
            {
                Id = id,
                Title = title,
                Content = content,
                OriginalContent = originalContent,
                ContentType = contentType
            };

            Tabs.Add(tab);
            ActivateTab(tab);
            OnPropertyChanged(nameof(HasTabs));
            return true;
        }

        public void ActivateTab(EditorTab tab)
        {
            if (Tabs.Contains(tab))
            {
                ActiveTab = tab;
            }
        }

        public bool CloseTab(EditorTab tab)
        {
            if (!Tabs.Contains(tab)) return false;

            if (tab.Id != HOMEPAGE_ID)
            {
                if (TabClosing != null && !TabClosing(tab))
                {
                    return false;
                }
            }

            var index = Tabs.IndexOf(tab);
            if (index < 0)
            {
                return false;
            }

            Tabs.Remove(tab);
            OnPropertyChanged(nameof(HasTabs));

            if (Tabs.Count > 0)
            {
                var newIndex = Math.Max(0, Math.Min(index, Tabs.Count - 1));
                ActivateTab(Tabs[newIndex]);
            }
            else
            {
                AddHomepageTab();
            }

            return true;
        }

        public bool CloseAllTabs()
        {
            var tabsToClose = Tabs.Where(t => t.Id != HOMEPAGE_ID).ToList();

            if (tabsToClose.Count == 0) return true;

            foreach (var tab in tabsToClose)
            {
                if (!CloseTab(tab)) return false;
            }

            if (!Tabs.Any(t => t.Id == HOMEPAGE_ID))
            {
                AddHomepageTab();
            }

            AllTabsClosed?.Invoke();

            return true;
        }

        public void UpdateTabContent(string id, string content)
        {
            var tab = Tabs.FirstOrDefault(t => t.Id == id);
            if (tab != null)
            {
                tab.Content = content;
            }
        }

        public void MarkTabSaved(string id, string savedContent)
        {
            var tab = Tabs.FirstOrDefault(t => t.Id == id);
            if (tab != null)
            {
                tab.OriginalContent = savedContent;
                tab.IsModified = false;

                if (tab.IsNew)
                {
                    var firstLine = savedContent.Split('\n').FirstOrDefault()?.Trim() ?? "";
                    if (firstLine.StartsWith("#"))
                    {
                        tab.Title = firstLine.TrimStart('#').Trim();
                    }
                    else
                    {
                        tab.Title = id;
                    }
                    tab.IsNew = false;
                    TM.App.Log($"[EditorTabManager] 新章节已保存，更新标题: {tab.Title}");
                }
            }
        }

        public bool IsAtLimit => Tabs.Count >= MaxTabs;

        public bool HasTab(string id) => Tabs.Any(t => t.Id == id);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
