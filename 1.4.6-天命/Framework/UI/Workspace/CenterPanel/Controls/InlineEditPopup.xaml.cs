using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.UI.Workspace.CenterPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class InlineEditPopup : UserControl
    {
        public event Action? PopupClosed;

        public event Action? Rejected;

        public InlineEditPopup()
        {
            InitializeComponent();
        }

        public void Show(string selectedText, Action<string, string> onAccept, Action<string, string> onShowDiff)
        {
            var viewModel = new InlineEditPopupViewModel(selectedText);
            viewModel.AcceptRequested += (original, modified) =>
            {
                onAccept?.Invoke(original, modified);
                Close();
            };
            viewModel.ShowDiffRequested += (original, modified) =>
            {
                onShowDiff?.Invoke(original, modified);
            };
            viewModel.Rejected += () =>
            {
                Rejected?.Invoke();
            };
            viewModel.CloseRequested += Close;

            DataContext = viewModel;
            Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Visibility = Visibility.Collapsed;
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            DataContext = null;
            PopupClosed?.Invoke();
        }
    }
}
