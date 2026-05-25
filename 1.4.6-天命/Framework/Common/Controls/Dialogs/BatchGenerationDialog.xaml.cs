using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.Controls.Dialogs
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class BatchGenerationDialog : Window
    {
        public bool? Result { get; private set; }

        public BatchGenerationConfig? Config { get; private set; }

        private string _categoryName = string.Empty;
        private int _totalCount = 10;
        private int _batchSize = 10;
        private bool _singleMode = false;

        public BatchGenerationDialog()
        {
            InitializeComponent();
        }

        public void SetCategoryName(string categoryName)
        {
            _categoryName = categoryName;
            if (CategoryNameText != null)
            {
                CategoryNameText.Text = categoryName;
            }

            if (TitleText != null)
            {
                TitleText.Text = _singleMode ? $"生成确认 - {categoryName}" : $"批量生成 - {categoryName}";
            }
        }

        public void SetSingleMode(bool singleMode)
        {
            _singleMode = singleMode;
            if (singleMode)
            {
                _totalCount = 1;
                _batchSize = 1;

                if (TotalCountRow != null) TotalCountRow.Visibility = Visibility.Collapsed;
                if (BatchSizeRow != null) BatchSizeRow.Visibility = Visibility.Collapsed;
                if (FormulaHintRow != null) FormulaHintRow.Visibility = Visibility.Collapsed;
                if (EstimatedBatchesRow != null) EstimatedBatchesRow.Visibility = Visibility.Collapsed;
                if (WarningBorder != null) WarningBorder.Visibility = Visibility.Collapsed;

                if (SingleModeHintBorder != null) SingleModeHintBorder.Visibility = Visibility.Visible;

                if (TitleText != null) TitleText.Text = $"生成确认 - {_categoryName}";
                if (ConfirmButton != null) ConfirmButton.Content = "确认生成";
            }
        }

        public void SetDefaults(int totalCount = 10, int batchSize = 10)
        {
            _totalCount = Math.Clamp(totalCount, 1, 9999);
            _batchSize = Math.Clamp(batchSize, 1, 100);
            if (TotalCountTextBox != null)
            {
                TotalCountTextBox.Text = _totalCount.ToString();
            }

            if (BatchSizeTextBox != null)
            {
                BatchSizeTextBox.Text = _batchSize.ToString();
            }
            UpdateEstimatedBatches();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TotalCountTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (int.TryParse(TotalCountTextBox.Text, out var value))
            {
                _totalCount = Math.Clamp(value, 1, 9999);
            }
            UpdateEstimatedBatches();
            UpdateWarning();
        }

        private void BatchSizeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (int.TryParse(BatchSizeTextBox.Text, out var value))
            {
                _batchSize = Math.Clamp(value, 1, 100);
            }
            UpdateEstimatedBatches();
        }

        private void UpdateEstimatedBatches()
        {
            if (_batchSize <= 0) _batchSize = 1;
            var batches = (int)Math.Ceiling((double)_totalCount / _batchSize);
            if (EstimatedBatchesText != null)
            {
                EstimatedBatchesText.Text = batches.ToString();
            }
        }

        private void UpdateWarning()
        {
            if (_totalCount > 500)
            {
                if (WarningBorder != null)
                {
                    WarningBorder.Visibility = Visibility.Visible;
                }

                if (WarningText != null)
                {
                    WarningText.Text = $"数量较大（{_totalCount}个），预计需要较长时间，确认继续？";
                }
            }
            else if (_totalCount > 100)
            {
                var batches = (int)Math.Ceiling((double)_totalCount / _batchSize);
                if (WarningBorder != null)
                {
                    WarningBorder.Visibility = Visibility.Visible;
                }

                if (WarningText != null)
                {
                    WarningText.Text = $"预计需要 {batches} 批次，耗时较长";
                }
            }
            else
            {
                if (WarningBorder != null)
                {
                    WarningBorder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Config = null;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            int totalCount;
            int batchSize;

            if (_singleMode)
            {
                totalCount = 1;
                batchSize = 1;
            }
            else
            {
                if (TotalCountTextBox == null || !int.TryParse(TotalCountTextBox.Text, out totalCount) || totalCount < 1 || totalCount > 9999)
                {
                    GlobalToast.Warning("输入错误", "生成数量必须在 1-9999 之间");
                    TotalCountTextBox?.Focus();
                    TotalCountTextBox?.SelectAll();
                    return;
                }

                if (BatchSizeTextBox == null || !int.TryParse(BatchSizeTextBox.Text, out batchSize) || batchSize < 1 || batchSize > 100)
                {
                    GlobalToast.Warning("输入错误", "单批数量必须在 1-100 之间");
                    BatchSizeTextBox?.Focus();
                    BatchSizeTextBox?.SelectAll();
                    return;
                }

                if (totalCount > 500)
                {
                    var confirmed = StandardDialog.ShowConfirm(
                        $"您即将生成 {totalCount} 个实体，预计需要 {(int)Math.Ceiling((double)totalCount / batchSize)} 批次，耗时较长。\n\n确定要继续吗？",
                        "大数量确认");
                    if (!confirmed)
                    {
                        return;
                    }
                }
            }

            Config = new BatchGenerationConfig
            {
                CategoryName = _categoryName,
                TotalCount = totalCount,
                BatchSize = batchSize
            };

            Result = true;
            Close();
        }

        public static BatchGenerationConfig? Show(
            string categoryName,
            int defaultTotalCount = 10,
            int defaultBatchSize = 10,
            Window? owner = null,
            bool singleMode = false)
        {
            try
            {
                var dialog = new BatchGenerationDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, owner);

                dialog.SetSingleMode(singleMode);
                dialog.SetCategoryName(categoryName);
                if (!singleMode)
                {
                    dialog.SetDefaults(defaultTotalCount, defaultBatchSize);
                }

                dialog.ShowDialog();

                return dialog.Result == true ? dialog.Config : null;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BatchGenerationDialog] Show failed: {ex}");
                GlobalToast.Error("弹窗错误", $"生成弹窗打开失败：{ex.Message}");
                return null;
            }
        }

        public static System.Threading.Tasks.Task<BatchGenerationConfig?> ShowAsync(
            string categoryName,
            int defaultTotalCount = 10,
            int defaultBatchSize = 10,
            Window? owner = null,
            bool singleMode = false)
        {
            return System.Threading.Tasks.Task.FromResult(Show(categoryName, defaultTotalCount, defaultBatchSize, owner, singleMode));
        }
    }
}
