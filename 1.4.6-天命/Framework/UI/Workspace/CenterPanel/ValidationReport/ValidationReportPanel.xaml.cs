using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Validation;
using ValidationReportModel = TM.Services.Modules.ProjectData.Models.Validation.ValidationReport;
using ValidationResultEnum = TM.Services.Modules.ProjectData.Models.Validation.ValidationResult;

namespace TM.Framework.UI.Workspace.CenterPanel.ValidationReport
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class ValidationReportPanel : UserControl
    {
        private readonly IValidationReportService _reportService;
        private string? _currentChapterId;
        private ValidationReportModel? _currentReport;

        public event EventHandler<ValidationReportModel>? ValidationCompleted;

        public ValidationReportPanel()
        {
            InitializeComponent();
            _reportService = ServiceLocator.Get<IValidationReportService>();
        }

        public async Task SetChapterAsync(string chapterId)
        {
            _currentChapterId = chapterId;

            var report = await _reportService.GetLatestReportAsync(chapterId);
            if (report != null)
            {
                DisplayReport(report);
            }
            else
            {
                ClearReport();
            }
        }

        public async Task ValidateAsync()
        {
            if (string.IsNullOrEmpty(_currentChapterId))
            {
                GlobalToast.Warning("提示", "请先选择要校验的章节");
                return;
            }

            try
            {
                StatusIcon.Text = "⏳";
                StatusText.Text = "校验中...";
                SummaryText.Text = "正在执行校验，请稍候";
                RefreshButton.IsEnabled = false;

                var report = await _reportService.ValidateChapterAsync(_currentChapterId);
                DisplayReport(report);

                ValidationCompleted?.Invoke(this, report);
                GlobalToast.Success("校验完成", report.Summary);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ValidationReportPanel] 校验失败: {ex.Message}");
                GlobalToast.Error("校验失败", ex.Message);
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private void DisplayReport(ValidationReportModel report)
        {
            _currentReport = report;

            switch (report.Result)
            {
                case ValidationResultEnum.Pass:
                    StatusIcon.Text = "✅";
                    StatusText.Text = "校验通过";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                    break;
                case ValidationResultEnum.Warning:
                    StatusIcon.Text = "⚠️";
                    StatusText.Text = "存在警告";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    break;
                case ValidationResultEnum.Error:
                    StatusIcon.Text = "❌";
                    StatusText.Text = "存在错误";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    break;
                default:
                    StatusIcon.Text = "⏳";
                    StatusText.Text = "待校验";
                    StatusText.Foreground = (Brush)FindResource("TextPrimary");
                    break;
            }

            SummaryText.Text = report.Summary;

            PassCountText.Text = report.PassCount.ToString();
            WarningCountText.Text = report.WarningCount.ToString();
            ErrorCountText.Text = report.ErrorCount.ToString();

            var items = new ObservableCollection<ValidationItemViewModel>();
            foreach (var item in report.Items)
            {
                items.Add(new ValidationItemViewModel(item));
            }
            ItemsListBox.ItemsSource = items;
        }

        private void ClearReport()
        {
            _currentReport = null;
            StatusIcon.Text = "⏳";
            StatusText.Text = "等待校验";
            StatusText.Foreground = (Brush)FindResource("TextPrimary");
            SummaryText.Text = "选择章节后点击校验";
            PassCountText.Text = "0";
            WarningCountText.Text = "0";
            ErrorCountText.Text = "0";
            ItemsListBox.ItemsSource = null;
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            _ = ValidateAsync();
        }
    }

    public class ValidationItemViewModel
    {
        private readonly ValidationItem _item;

        public ValidationItemViewModel(ValidationItem item)
        {
            _item = item;
        }

        public string ValidationType => _item.ValidationType;
        public string Name => _item.Name;
        public string Details => _item.Details;
        public string Suggestion => _item.Suggestion;
        public bool HasSuggestion => !string.IsNullOrEmpty(_item.Suggestion);

        public string ResultIcon => _item.Result switch
        {
            ValidationItemResult.Pass => "✓",
            ValidationItemResult.Warning => "!",
            ValidationItemResult.Error => "✗",
            ValidationItemResult.Skipped => "—",
            _ => "?"
        };

        public Brush ResultBackground => _item.Result switch
        {
            ValidationItemResult.Pass => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E")),
            ValidationItemResult.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            ValidationItemResult.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            ValidationItemResult.Skipped => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
        };
    }
}
