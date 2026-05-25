using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class ProjectSpecPanel : UserControl
    {
        public ProjectSpecPanel()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<ProjectSpecPanelViewModel>();
        }

        private void OnPolishHelpClick(object sender, RoutedEventArgs e)
        {
            StandardDialog.ShowInfo(BuildPolishHelpText(), "润色模式 · 功能注释", Window.GetWindow(this));
        }

        private static string BuildPolishHelpText()
        {
            return string.Join("\n", new[]
            {
                "【字数控制】AI 写的字数和你设的目标对不上时怎么办。",
                "  • 关闭：按你设的字数检查，少写或多写都会触发重写。",
                "  • 全局补偿：自动让 AI 多写一点（部分模型容易少写），结果更接近目标。",
                "  • 全局免检：不管字数，AI 写多少都行。",
                "",
                "【润色控制】生成或润色出问题时（写废了/字数对不上/格式坏了）怎么办。",
                "  • 使用原文：保留 AI 原本的版本，可能有英文标点等小毛病。",
                "  • 正则降级（推荐）：自动用本地规则清洗一遍（中文引号、删除套话），尽量补救。",
                "  • 终止落盘：直接放弃本章，停止后续任务，需要你手动处理。",
                "",
                "【润色次数】是否对生成结果再做一遍润色。",
                "  • 关闭润色：不润色，AI 写完直接保存。",
                "  • 一次润色：润一遍（推荐，质量和速度平衡）。",
                "  • 二次润色：连续润两遍，文笔更稳但速度慢一倍。",
                "",
                "【润色模型】润色时用什么方式优化。",
                "  • 本地正则：只用本地规则清洗（中文引号、AI 套话删除），不联网、零成本。",
                "  • 在线润色：再调一次 AI 做文学化重写，效果最好但需要 API 调用。",
                "",
                "【小贴士】",
                "  • 字数控制和润色控制各管各的，可以自由搭配。",
                "  • 不知道选什么 → 全部保持默认即可。",
            });
        }
    }
}
