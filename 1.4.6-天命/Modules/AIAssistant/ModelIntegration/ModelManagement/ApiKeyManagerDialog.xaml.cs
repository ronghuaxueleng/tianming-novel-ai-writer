using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Helpers.Id;
using TM.Services.Framework.AI.Core;
using TM.Framework.Common.Helpers;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ApiKeyManagerDialog : Window
{
    public enum KeyHealthStatus { Active, RateLimited, Disabled }

    public class KeyItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; Notify(nameof(IsEnabled)); Notify(nameof(StatusIcon)); Notify(nameof(StatusText)); }
        }

        private KeyHealthStatus _healthStatus = KeyHealthStatus.Active;
        public KeyHealthStatus HealthStatus
        {
            get => _healthStatus;
            set { _healthStatus = value; Notify(nameof(StatusIcon)); Notify(nameof(StatusText)); }
        }

        public string StatusIcon => HealthStatus switch
        {
            KeyHealthStatus.RateLimited => "⚠️",
            _ => IsEnabled ? "✅" : "⛔"
        };

        public string StatusText => HealthStatus switch
        {
            KeyHealthStatus.RateLimited => "限速中",
            _ => IsEnabled ? "启用" : "禁用"
        };

        private bool _isPlainVisible;
        public bool IsPlainVisible
        {
            get => _isPlainVisible;
            set { _isPlainVisible = value; Notify(nameof(DisplayKey)); }
        }

        public string MaskedKey
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Key)) return "(空)";
                return new string('*', Math.Min(Key.Length, 20));
            }
        }

        public string DisplayKey => IsPlainVisible ? Key : MaskedKey;

        private void Notify(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<KeyItem> _keys = new();

    public List<ApiKeyEntry>? ResultKeys { get; private set; }

    public ApiKeyManagerDialog(List<ApiKeyEntry>? sourceKeys, string providerName, string? providerId = null)
    {
        InitializeComponent();

        TitleText.Text = $"API 密钥管理 - {providerName}";

        KeyPoolStatus? poolStatus = null;
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            try
            {
                var rotation = ServiceLocator.Get<ApiKeyRotationService>();
                poolStatus = rotation.GetPoolStatus(providerId);
            }
            catch { }
        }

        if (sourceKeys != null)
        {
            foreach (var k in sourceKeys)
            {
                var entryStatus = poolStatus?.Entries.Find(e => e.KeyId == k.Id);
                KeyHealthStatus health;
                if (!k.IsEnabled)
                    health = KeyHealthStatus.Disabled;
                else if (entryStatus?.Status == KeyEntryStatus.PermanentlyDisabled)
                    health = KeyHealthStatus.Disabled;
                else if (entryStatus?.Status == KeyEntryStatus.TemporarilyDisabled)
                    health = KeyHealthStatus.RateLimited;
                else
                    health = KeyHealthStatus.Active;

                _keys.Add(new KeyItem
                {
                    Id = k.Id,
                    Key = k.Key,
                    Remark = k.Remark,
                    IsEnabled = k.IsEnabled,
                    HealthStatus = health
                });
            }
        }

        KeyList.ItemsSource = _keys;
        _keys.CollectionChanged += (_, _) => RefreshUI();
        RefreshUI();
    }

    private bool _showAllPlain;
    private bool _batchExpanded;

    private void RefreshUI()
    {
        var total = _keys.Count;
        var enabled = _keys.Count(k => k.IsEnabled);
        KeyListBorder.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatsText.Text = total > 0 ? $"共 {total} 个密钥，{enabled} 个启用" : "暂无密钥，请在上方添加";
    }

    private void ToggleShowAll_Click(object sender, RoutedEventArgs e)
    {
        _showAllPlain = !_showAllPlain;
        foreach (var k in _keys)
            k.IsPlainVisible = _showAllPlain;
        ToggleAllIcon.Text = _showAllPlain ? "👁" : "🔒";
        ToggleAllText.Text = _showAllPlain ? " 一键隐藏" : " 一键显示";
    }

    private void BatchImportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_batchExpanded)
        {
            _batchExpanded = true;
            BatchImportArea.Visibility = Visibility.Visible;
            BatchInput.Focus();
            return;
        }

        var batchText = BatchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(batchText))
        {
            _batchExpanded = false;
            BatchImportArea.Visibility = Visibility.Collapsed;
            BatchImportBtnText.Text = "一键导入";
            return;
        }

        var lines = batchText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Replace('，', ',').TrimEnd(',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existing = new HashSet<string>(_keys.Select(k => k.Key), StringComparer.Ordinal);
        var added = 0;
        foreach (var key in lines)
        {
            if (existing.Contains(key)) continue;
            _keys.Add(new KeyItem
            {
                Id = ShortIdGenerator.New("K"),
                Key = key,
                Remark = $"导入 #{_keys.Count + 1}",
                IsEnabled = true
            });
            existing.Add(key);
            added++;
        }

        BatchInput.Text = string.Empty;
        _batchExpanded = false;
        BatchImportArea.Visibility = Visibility.Collapsed;
        BatchImportBtnText.Text = "一键导入";

        if (added > 0)
            TM.Framework.Common.Helpers.GlobalToast.Success("导入成功", $"已导入 {added} 个密钥");
        else
            TM.Framework.Common.Helpers.GlobalToast.Info("密钥已存在", "所有密钥均已存在");
    }

    private void BatchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var text = BatchInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            BatchImportBtnText.Text = "一键导入";
            StatsText.Text = _keys.Count > 0
                ? $"共 {_keys.Count} 个密钥，{_keys.Count(k => k.IsEnabled)} 个启用"
                : "暂无密钥，请在上方添加";
            return;
        }

        var count = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Replace('，', ',').TrimEnd(',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .Count();

        BatchImportBtnText.Text = "一键解析";
        StatsText.Text = $"检测到 {count} 条密钥";
    }

    private void AddKey_Click(object sender, RoutedEventArgs e)
    {
        AddKeysFromInput();
    }

    private void NewKeyInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AddKeysFromInput();
            e.Handled = true;
        }
    }

    private void AddKeysFromInput()
    {
        var input = NewKeyInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        var newKeys = input.Replace('，', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingValues = new HashSet<string>(_keys.Select(k => k.Key), StringComparer.Ordinal);
        var added = 0;

        foreach (var key in newKeys)
        {
            if (existingValues.Contains(key)) continue;
            _keys.Add(new KeyItem
            {
                Id = ShortIdGenerator.New("K"),
                Key = key,
                Remark = $"密钥 #{_keys.Count + 1}",
                IsEnabled = true
            });
            existingValues.Add(key);
            added++;
        }

        NewKeyInput.Text = string.Empty;
        NewKeyInput.Focus();

        if (added == 0 && newKeys.Count > 0)
        {
            TM.Framework.Common.Helpers.GlobalToast.Info("密钥已存在", "所有输入的密钥均已存在");
        }
    }

    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string keyId)
        {
            var item = _keys.FirstOrDefault(k => k.Id == keyId);
            if (item != null)
            {
                _keys.Remove(item);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ResultKeys = _keys.Select(k => new ApiKeyEntry
        {
            Id = k.Id,
            Key = k.Key,
            Remark = k.Remark,
            IsEnabled = k.IsEnabled,
            CreatedAt = DateTime.Now
        }).ToList();

        DialogResult = true;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
