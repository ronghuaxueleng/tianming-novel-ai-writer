using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TM.Framework.Common.Helpers.MVVM
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task>? _execute;
        private readonly Func<object?, Task>? _executeWithParameter;
        private readonly Func<bool>? _canExecute;
        private readonly Func<object?, bool>? _canExecuteWithParameter;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        {
            _executeWithParameter = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecuteWithParameter = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting)
            {
                return false;
            }

            if (_canExecuteWithParameter != null)
            {
                return _canExecuteWithParameter(parameter);
            }

            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _isExecuting = true;
            RaiseCanExecuteChanged();

            _ = ExecuteAsync(parameter);
        }

        private async Task ExecuteAsync(object? parameter)
        {
            try
            {
                if (_execute != null)
                {
                    await _execute();
                }
                else if (_executeWithParameter != null)
                {
                    await _executeWithParameter(parameter);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AsyncRelayCommand] 执行失败: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        private static volatile bool _invalidatePending;

        public void RaiseCanExecuteChanged()
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
    }
}

