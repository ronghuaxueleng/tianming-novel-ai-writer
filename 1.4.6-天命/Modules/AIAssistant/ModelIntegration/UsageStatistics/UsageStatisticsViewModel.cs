using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Services.Framework.AI.Monitoring;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Modules.AIAssistant.ModelIntegration.UsageStatistics;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class UsageStatisticsViewModel : INotifyPropertyChanged
{
    private readonly IAIUsageStatisticsService _statisticsService;
    private StatisticsSummary _summary = new();
    private ObservableCollection<DailyStatistics> _dailyStats = new();
    private ObservableCollection<ModelStatItem> _modelStats = new();
    private ObservableCollection<ApiCallRecord> _recentCalls = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public StatisticsSummary Summary
    {
        get => _summary;
        set { _summary = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DailyStatistics> DailyStats
    {
        get => _dailyStats;
        set { _dailyStats = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ModelStatItem> ModelStats
    {
        get => _modelStats;
        set { _modelStats = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ApiCallRecord> RecentCalls
    {
        get => _recentCalls;
        set { _recentCalls = value; OnPropertyChanged(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand { get; }

    public UsageStatisticsViewModel(IAIUsageStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;

        RefreshCommand = new RelayCommand(() => LoadStatistics(showToast: true));
        ClearCommand = new RelayCommand(ClearStatistics);

        LoadStatistics(showToast: false);
    }

    private void LoadStatistics(bool showToast = false)
    {
        try
        {
            Summary = _statisticsService.GetSummary();

            var dailyData = _statisticsService.GetDailyStatistics(7);
            DailyStats = new ObservableCollection<DailyStatistics>(dailyData);

            var modelData = _statisticsService.GetStatisticsByModel();
            var modelItems = modelData.Select(kv => new ModelStatItem
            {
                ModelName = kv.Key,
                TotalCalls = kv.Value.TotalCalls,
                SuccessRate = kv.Value.SuccessRate,
                AverageResponseTime = kv.Value.AverageResponseTime
            }).OrderByDescending(m => m.TotalCalls);
            ModelStats = new ObservableCollection<ModelStatItem>(modelItems);

            var recentData = _statisticsService.GetRecentRecords(50);
            RecentCalls = new ObservableCollection<ApiCallRecord>(recentData);

            TM.App.Log("[UsageStatistics] 统计数据已刷新");
            if (showToast) GlobalToast.Success("已刷新", "统计数据已更新");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[UsageStatistics] 加载统计失败: {ex.Message}");
            GlobalToast.Error("加载失败", ex.Message);
        }
    }

    private void ClearStatistics()
    {
        try
        {
            var confirm = StandardDialog.ShowConfirm("确定要清空所有统计数据吗？\n此操作不可恢复！", "清空统计");
            if (confirm == true)
            {
                _statisticsService.ClearStatistics();
                LoadStatistics();
                TM.App.Log("[UsageStatistics] 统计数据已清空");
                GlobalToast.Success("已清空", "统计数据已清空");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[UsageStatistics] 清空统计失败: {ex.Message}");
            GlobalToast.Error("操作失败", ex.Message);
        }
    }
}

public class ModelStatItem
{
    public string ModelName { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTime { get; set; }
}
