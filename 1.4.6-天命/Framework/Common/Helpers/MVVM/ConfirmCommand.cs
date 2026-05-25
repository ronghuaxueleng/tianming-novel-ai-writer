using System;
using System.Windows;
using System.Windows.Input;

namespace TM.Framework.Common.Helpers.MVVM
{
    public class ConfirmCommand : ICommand
    {
        private readonly ICommand _originalCommand;
        private readonly string _confirmMessage;
        private readonly string _confirmTitle;

        public ConfirmCommand(ICommand originalCommand, string confirmMessage, string confirmTitle = "确认")
        {
            _originalCommand = originalCommand ?? throw new ArgumentNullException(nameof(originalCommand));
            _confirmMessage = confirmMessage ?? throw new ArgumentNullException(nameof(confirmMessage));
            _confirmTitle = confirmTitle ?? "确认";
        }

        public event EventHandler? CanExecuteChanged
        {
            add => _originalCommand.CanExecuteChanged += value;
            remove => _originalCommand.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter)
        {
            return _originalCommand.CanExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            var result = StandardDialog.ShowConfirm(
                _confirmMessage,
                _confirmTitle
            );

            if (result)
            {
                _originalCommand.Execute(parameter);
            }
        }
    }
}

