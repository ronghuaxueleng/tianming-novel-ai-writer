using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
[Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
public partial class AlertView : UserControl
{
    private AlertViewModel? ViewModel => DataContext as AlertViewModel;

    private AlertViewModel? _subscribedViewModel;
    private bool _suppressPasswordSync;

    public AlertView()
    {
        InitializeComponent();
        DataContext = ServiceLocator.Get<AlertViewModel>();
        AttachViewModel(ViewModel);
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        AttachViewModel(e.NewValue as AlertViewModel);
        SyncPasswordFromViewModel();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
    }

    private void AttachViewModel(AlertViewModel? vm)
    {
        if (vm == null || ReferenceEquals(_subscribedViewModel, vm)) return;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        _subscribedViewModel = vm;
    }

    private void DetachViewModel()
    {
        if (_subscribedViewModel == null) return;
        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlertViewModel.AuthCode))
        {
            SyncPasswordFromViewModel();
        }
    }

    private void AuthCodePasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        SyncPasswordFromViewModel();
    }

    private void SyncPasswordFromViewModel()
    {
        if (ViewModel == null || AuthCodePasswordBox == null) return;
        var current = AuthCodePasswordBox.Password ?? string.Empty;
        var target = ViewModel.AuthCode ?? string.Empty;
        if (current == target) return;

        _suppressPasswordSync = true;
        try { AuthCodePasswordBox.Password = target; }
        finally { _suppressPasswordSync = false; }
    }

    private void AuthCodePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressPasswordSync) return;
        if (sender is PasswordBox pb && ViewModel != null)
        {
            ViewModel.AuthCode = pb.Password ?? string.Empty;
        }
    }
}
