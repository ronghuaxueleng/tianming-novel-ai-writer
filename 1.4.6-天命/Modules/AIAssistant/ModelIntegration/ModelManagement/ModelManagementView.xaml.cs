using System;
using System.Reflection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Controls.DataManagement;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public partial class ModelManagementView : UserControl
{
    private bool _isPasswordUpdating;
    private PasswordBox? _apiKeyPasswordBox;

    public ModelManagementView(ModelManagementViewModel viewModel)
    {
        try
        {
            InitializeComponent();
            DataContext = viewModel;

            if (RootLayout?.HeaderTabs != null)
            {
                if (RootLayout.HeaderTabs.Count >= 1)
                {
                    RootLayout.HeaderTabs[0].Header = "详细信息";
                    RootLayout.HeaderTabs[0].Icon = "📝";
                }

                if (RootLayout.HeaderTabs.Count >= 2)
                {
                    RootLayout.HeaderTabs[1].Header = "参数与速率";
                    RootLayout.HeaderTabs[1].Icon = "⚙️";
                }

                if (RootLayout.HeaderTabs.Count < 3)
                {
                    RootLayout.HeaderTabs.Add(new TabItemData
                    {
                        Header = "全局参数",
                        Icon = "🔧",
                        IsSelected = false
                    });
                }

                if (RootLayout.HeaderTabs.Count >= 3 && viewModel is ModelManagementViewModel vmInit)
                {
                    RootLayout.HeaderTabs[2].IsEnabled = vmInit.IsGlobalParametersAvailable;
                    if (!vmInit.IsGlobalParametersAvailable && RootLayout.SelectedHeaderTabIndex == 2)
                    {
                        RootLayout.SelectedHeaderTabIndex = 0;
                    }
                }
            }

            if (viewModel is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += ViewModel_PropertyChanged;
            }

            TM.App.Log("[ModelManagement] 模型管理视图已加载（方案2标准）");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[ModelManagement] 初始化失败: {ex.Message}");
            throw;
        }
    }

    private void ApiKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            _apiKeyPasswordBox = passwordBox;
            if (DataContext is ModelManagementViewModel viewModel && !string.IsNullOrEmpty(viewModel.FormApiKey))
            {
                _isPasswordUpdating = true;
                passwordBox.Password = viewModel.FormApiKey;
                _isPasswordUpdating = false;
            }
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isPasswordUpdating) return;
        if (sender is PasswordBox passwordBox && DataContext is ModelManagementViewModel viewModel)
        {
            _isPasswordUpdating = true;
            viewModel.FormApiKey = passwordBox.Password;
            _isPasswordUpdating = false;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ModelManagementViewModel vm)
            return;

        if (e.PropertyName == nameof(ModelManagementViewModel.FormApiKey) && !_isPasswordUpdating)
        {
            if (_apiKeyPasswordBox != null)
            {
                _isPasswordUpdating = true;
                _apiKeyPasswordBox.Password = vm.FormApiKey ?? string.Empty;
                _isPasswordUpdating = false;
            }
        }

        if (e.PropertyName == nameof(ModelManagementViewModel.IsGlobalParametersAvailable) && RootLayout?.HeaderTabs != null)
        {
            if (RootLayout.HeaderTabs.Count >= 3)
            {
                bool available = vm.IsGlobalParametersAvailable;
                RootLayout.HeaderTabs[2].IsEnabled = available;

                if (!available && RootLayout.SelectedHeaderTabIndex == 2)
                {
                    RootLayout.SelectedHeaderTabIndex = 0;
                }
            }
        }
    }

}
