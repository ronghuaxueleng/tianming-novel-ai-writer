using System;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TM.Modules.AIAssistant.ModelIntegration.UsageStatistics;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class UsageStatisticsView : UserControl
{
    public UsageStatisticsView(UsageStatisticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TM.App.Log("[UsageStatistics] 视图已加载");
    }
}

public class SuccessToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool success)
        {
            return success ? "✓ 成功" : "✗ 失败";
        }
        return "未知";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
