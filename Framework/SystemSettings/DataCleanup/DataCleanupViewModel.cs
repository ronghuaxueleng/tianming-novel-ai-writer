using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;
using TM.Framework.SystemSettings.DataCleanup.Models;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.SystemSettings.DataCleanup
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DataCleanupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly AIService _aiService;
        private readonly SessionManager _sessionManager;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, string message, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[DataCleanup] {key}: {message} - {ex.Message}");
        }

        private bool _isLoading;
        private string _statusMessage = "";

        public DataCleanupViewModel(AIService aiService, SessionManager sessionManager)
        {
            _aiService = aiService;
            _sessionManager = sessionManager;
            Modules = new RangeObservableCollection<CleanupModule>();

            CleanupCommand = new RelayCommand(() => ExecuteCleanup().SafeFireAndForget(ex => TM.App.Log($"[DataCleanupViewModel] {ex.Message}")), () => CanCleanup);
            SelectAllCommand = new RelayCommand(SelectAll);
            SelectNoneCommand = new RelayCommand(SelectNone);
            SelectModuleCommand = new RelayCommand(param => SelectModule(param as CleanupModule));
            RefreshCommand = new RelayCommand(() => LoadModules().SafeFireAndForget(ex => TM.App.Log($"[DataCleanupViewModel] {ex.Message}")));

            LoadModules().SafeFireAndForget(ex => TM.App.Log($"[DataCleanupViewModel] {ex.Message}"));
        }

        #region 属性

        public RangeObservableCollection<CleanupModule> Modules { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        public bool CanCleanup => Modules.Any(m => m.Items.Any(i => i.IsSelected));

        #endregion

        #region 命令

        public ICommand CleanupCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectModuleCommand { get; }
        public ICommand RefreshCommand { get; }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
