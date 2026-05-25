using System;
using System.Windows.Input;

namespace TM.Framework.Common.Helpers.MVVM
{
    public class RelayCommand : ICommand
    {
        private readonly Action? _execute;
        private readonly Action<object?>? _executeWithParam;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            _executeWithParam = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter)
        {
            if (_execute != null)
            {
                _execute();
            }
            else if (_executeWithParam != null)
            {
                _executeWithParam(parameter);
            }
        }

        private static volatile bool _invalidatePending;

        internal static void StaticRaiseCanExecuteChanged()
        {
            if (_invalidatePending) return;
            _invalidatePending = true;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                (System.Action)(() =>
                {
                    _invalidatePending = false;
                    CommandManager.InvalidateRequerySuggested();
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        public void RaiseCanExecuteChanged()
        {
            StaticRaiseCanExecuteChanged();
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            if (parameter is T typedParameter)
                return _canExecute?.Invoke(typedParameter) ?? true;
            if (parameter == null)
                return _canExecute?.Invoke(default) ?? true;
            return false;
        }

        public void Execute(object? parameter)
        {
            if (parameter is T typedParameter)
                _execute(typedParameter);
            else if (parameter == null)
                _execute(default);
        }

        public void RaiseCanExecuteChanged()
        {
            RelayCommand.StaticRaiseCanExecuteChanged();
        }
    }
}

