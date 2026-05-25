using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Common.ViewModels
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public abstract class SinglePageViewModelBase : INotifyPropertyChanged
    {

        private bool _isBusy;
        private bool _isLoading;
        private bool _hasUnsavedChanges;
        private string _statusMessage = string.Empty;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshCommand { get; }

        protected SinglePageViewModelBase()
        {
            SaveCommand = new RelayCommand(Save, CanSave);
            DeleteCommand = new RelayCommand(Delete, CanDelete);
            CancelCommand = new RelayCommand(Cancel);
            RefreshCommand = new RelayCommand(Refresh);

            OnInitialize();
        }

        protected virtual void OnInitialize()
        {
        }

        protected virtual void Save()
        {
            TM.App.Log($"[{GetType().Name}] Save未实现，子类需要重写此方法");
            GlobalToast.Warning("未实现", "保存功能未实现");
        }

        protected virtual bool CanSave()
        {
            return HasUnsavedChanges;
        }

        protected virtual void Delete()
        {
            TM.App.Log($"[{GetType().Name}] Delete未实现，子类需要重写此方法");
            GlobalToast.Warning("未实现", "删除功能未实现");
        }

        protected virtual bool CanDelete()
        {
            return true;
        }

        protected virtual void Cancel()
        {
            HasUnsavedChanges = false;
            StatusMessage = "已取消";
        }

        protected virtual void Refresh()
        {
            OnInitialize();
            StatusMessage = "已刷新";
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);

            if (propertyName != nameof(IsBusy) && 
                propertyName != nameof(IsLoading) && 
                propertyName != nameof(HasUnsavedChanges) &&
                propertyName != nameof(StatusMessage))
            {
                HasUnsavedChanges = true;
            }

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

