using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TodoOverlay : UserControl
    {
        private PanelCommunicationService? _panelComm;
        private PanelCommunicationService PanelComm => _panelComm ??= ServiceLocator.Get<PanelCommunicationService>();

        public event EventHandler? CloseRequested;

        public event EventHandler<TodoStepViewModel>? StepRequested;

        public TodoOverlay()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnBackToPlanClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is TodoPanelViewModel vm && vm.CanBackToPlan)
            {
                PanelComm.PublishShowPlanViewChanged(true);
            }
        }

        private void OnStepMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TodoStepViewModel step)
            {
                if (DataContext is TodoPanelViewModel vm)
                {
                    vm.SelectedStep = step;
                }

                if (e.ClickCount == 2)
                {
                    StepRequested?.Invoke(this, step);
                    e.Handled = true;
                }
            }
        }
    }
}
