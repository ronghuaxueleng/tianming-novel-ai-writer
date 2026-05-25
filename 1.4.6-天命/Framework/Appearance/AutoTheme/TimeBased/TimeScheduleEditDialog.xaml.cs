using System;
using System.Reflection;
using System.Windows;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TimeScheduleEditDialog : Window
    {
        public TimeScheduleItem? Result { get; private set; }

        public TimeScheduleEditDialog(TimeScheduleItem? item = null)
        {
            InitializeComponent();

            foreach (ThemeType theme in Enum.GetValues(typeof(ThemeType)))
            {
                ThemeComboBox.Items.Add(theme);
            }

            if (item != null)
            {
                StartHourTextBox.Text = item.StartTime.Hours.ToString("D2");
                StartMinuteTextBox.Text = item.StartTime.Minutes.ToString("D2");
                EndHourTextBox.Text = item.EndTime.Hours.ToString("D2");
                EndMinuteTextBox.Text = item.EndTime.Minutes.ToString("D2");
                ThemeComboBox.SelectedItem = item.TargetTheme;
                PrioritySlider.Value = item.Priority;
                UseTransitionCheckBox.IsChecked = item.UseTransition;
                DescriptionTextBox.Text = item.Description;

                MondayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Monday) != 0;
                TuesdayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Tuesday) != 0;
                WednesdayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Wednesday) != 0;
                ThursdayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Thursday) != 0;
                FridayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Friday) != 0;
                SaturdayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Saturday) != 0;
                SundayCheckBox.IsChecked = (item.EnabledWeekdays & Weekday.Sunday) != 0;
            }
            else
            {
                StartHourTextBox.Text = "09";
                StartMinuteTextBox.Text = "00";
                EndHourTextBox.Text = "18";
                EndMinuteTextBox.Text = "00";
                ThemeComboBox.SelectedIndex = 0;
                PrioritySlider.Value = 5;
                UseTransitionCheckBox.IsChecked = true;
                DescriptionTextBox.Text = "新的时间段";
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(StartHourTextBox.Text, out var startHour) || startHour < 0 || startHour > 23)
                {
                    StandardDialog.ShowWarning("开始时间的小时必须在0-23之间", "输入错误");
                    StartHourTextBox.Focus();
                    return;
                }

                if (!int.TryParse(StartMinuteTextBox.Text, out var startMinute) || startMinute < 0 || startMinute > 59)
                {
                    StandardDialog.ShowWarning("开始时间的分钟必须在0-59之间", "输入错误");
                    StartMinuteTextBox.Focus();
                    return;
                }

                if (!int.TryParse(EndHourTextBox.Text, out var endHour) || endHour < 0 || endHour > 23)
                {
                    StandardDialog.ShowWarning("结束时间的小时必须在0-23之间", "输入错误");
                    EndHourTextBox.Focus();
                    return;
                }

                if (!int.TryParse(EndMinuteTextBox.Text, out var endMinute) || endMinute < 0 || endMinute > 59)
                {
                    StandardDialog.ShowWarning("结束时间的分钟必须在0-59之间", "输入错误");
                    EndMinuteTextBox.Focus();
                    return;
                }

                if (ThemeComboBox.SelectedItem == null)
                {
                    StandardDialog.ShowWarning("请选择一个主题", "输入错误");
                    ThemeComboBox.Focus();
                    return;
                }

                Weekday enabledWeekdays = Weekday.None;
                if (MondayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Monday;
                if (TuesdayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Tuesday;
                if (WednesdayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Wednesday;
                if (ThursdayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Thursday;
                if (FridayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Friday;
                if (SaturdayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Saturday;
                if (SundayCheckBox.IsChecked == true) enabledWeekdays |= Weekday.Sunday;

                if (enabledWeekdays == Weekday.None)
                {
                    StandardDialog.ShowWarning("请至少选择一个星期", "输入错误");
                    return;
                }

                Result = new TimeScheduleItem
                {
                    StartTime = new TimeSpan(startHour, startMinute, 0),
                    EndTime = new TimeSpan(endHour, endMinute, 0),
                    TargetTheme = (ThemeType)ThemeComboBox.SelectedItem,
                    Priority = (int)PrioritySlider.Value,
                    UseTransition = UseTransitionCheckBox.IsChecked == true,
                    EnabledWeekdays = enabledWeekdays,
                    Description = DescriptionTextBox.Text?.Trim() ?? "未命名时间段"
                };

                App.Log($"[TimeScheduleEditDialog] 时间段编辑成功: {Result.Description}");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                App.Log($"[TimeScheduleEditDialog] 保存失败: {ex.Message}");
                StandardDialog.ShowError($"保存失败：{ex.Message}", "错误");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

