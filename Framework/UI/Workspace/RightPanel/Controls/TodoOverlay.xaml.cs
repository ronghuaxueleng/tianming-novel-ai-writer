using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class TodoOverlay : UserControl
    {
        private PanelCommunicationService? _panelComm;
        private PanelCommunicationService PanelComm => _panelComm ??= ServiceLocator.Get<PanelCommunicationService>();

        private INotifyCollectionChanged? _subscribedSteps;

        public event EventHandler? CloseRequested;

        public TodoOverlay()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_subscribedSteps != null)
            {
                _subscribedSteps.CollectionChanged -= OnStepsCollectionChanged;
                _subscribedSteps = null;
            }

            if (DataContext is TodoPanelViewModel vm)
            {
                vm.Steps.CollectionChanged += OnStepsCollectionChanged;
                _subscribedSteps = vm.Steps;
            }
        }

        private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add &&
                e.Action != NotifyCollectionChangedAction.Reset)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                StepsScrollViewer?.ScrollToBottom();
            }), DispatcherPriority.Background);
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
    }
}
