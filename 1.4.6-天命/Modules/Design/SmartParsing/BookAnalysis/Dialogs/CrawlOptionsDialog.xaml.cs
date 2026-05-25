using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Windows;
using TM.Framework.Common.Controls.Dialogs;
using TM.Modules.Design.SmartParsing.BookAnalysis.Crawler;
using ChapterInfo = TM.Modules.Design.SmartParsing.BookAnalysis.Crawler.ChapterInfo;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Dialogs
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CrawlOptionsDialog : Window
    {
        public CrawlOptions? Options { get; private set; }

        public new bool? DialogResult { get; private set; }

        private readonly List<ChapterInfo> _chapters;

        public CrawlOptionsDialog(List<ChapterInfo> chapters)
        {
            InitializeComponent();
            _chapters = chapters;

            var vipCount = chapters.FindAll(c => c.IsVip).Count;
            var normalCount = chapters.Count - vipCount;
            ChapterSummaryText.Text = $"共 {chapters.Count} 章（普通 {normalCount} 章，VIP {vipCount} 章）";

            if (chapters.Count > 0)
            {
                RangeEndBox.Text = chapters.Count.ToString();
            }
        }

        public static CrawlOptions? Show(Window? owner, List<ChapterInfo> chapters)
        {
            var dialog = new CrawlOptionsDialog(chapters);
            StandardDialog.EnsureOwnerAndTopmost(dialog, owner);

            dialog.ShowDialog();

            return dialog.DialogResult == true ? dialog.Options : null;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Options = BuildOptions();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private CrawlOptions BuildOptions()
        {
            var options = new CrawlOptions
            {
                SkipVipChapters = SkipVipCheckBox.IsChecked == true
            };

            if (AllRadio.IsChecked == true)
            {
                options.Mode = CrawlMode.All;
            }
            else if (FirstNRadio.IsChecked == true)
            {
                options.Mode = CrawlMode.FirstN;
                if (int.TryParse(FirstNCountBox.Text, out var count))
                {
                    options.FirstNCount = count;
                }
            }
            else if (RangeRadio.IsChecked == true)
            {
                options.Mode = CrawlMode.Range;
                if (int.TryParse(RangeStartBox.Text, out var start))
                {
                    options.RangeStart = start;
                }
                if (int.TryParse(RangeEndBox.Text, out var end))
                {
                    options.RangeEnd = end;
                }
            }

            return options;
        }
    }
}
