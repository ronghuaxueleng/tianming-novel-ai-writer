using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class ButtonHelper
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(ButtonHelper),
                new PropertyMetadata(false));

        public static bool GetIsSelected(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsSelectedProperty);
        }

        public static void SetIsSelected(DependencyObject obj, bool value)
        {
            obj.SetValue(IsSelectedProperty, value);
        }

        public static readonly DependencyProperty ConfirmMessageProperty =
            DependencyProperty.RegisterAttached(
                "ConfirmMessage",
                typeof(string),
                typeof(ButtonHelper),
                new PropertyMetadata(null, OnConfirmMessageChanged));

        public static string GetConfirmMessage(DependencyObject obj)
        {
            return (string)obj.GetValue(ConfirmMessageProperty);
        }

        public static void SetConfirmMessage(DependencyObject obj, string value)
        {
            obj.SetValue(ConfirmMessageProperty, value);
        }

        public static readonly DependencyProperty ConfirmTitleProperty =
            DependencyProperty.RegisterAttached(
                "ConfirmTitle",
                typeof(string),
                typeof(ButtonHelper),
                new PropertyMetadata("确认"));

        public static string GetConfirmTitle(DependencyObject obj)
        {
            return (string)obj.GetValue(ConfirmTitleProperty);
        }

        public static void SetConfirmTitle(DependencyObject obj, string value)
        {
            obj.SetValue(ConfirmTitleProperty, value);
        }

        private static readonly DependencyProperty OriginalCommandProperty =
            DependencyProperty.RegisterAttached(
                "OriginalCommand",
                typeof(ICommand),
                typeof(ButtonHelper),
                new PropertyMetadata(null));

        private static ICommand GetOriginalCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(OriginalCommandProperty);
        }

        private static void SetOriginalCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(OriginalCommandProperty, value);
        }

        private static void OnConfirmMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button button) return;

            var message = e.NewValue as string;

            if (string.IsNullOrEmpty(message))
            {
                var originalCommand = GetOriginalCommand(button);
                if (originalCommand != null)
                {
                    button.Command = originalCommand;
                    SetOriginalCommand(button, null!);
                }
                return;
            }

            if (button.Command != null && button.Command is not ConfirmCommand)
            {
                WrapCommandWithConfirm(button);
            }

            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                Button.CommandProperty, 
                typeof(Button));

            descriptor?.AddValueChanged(button, (s, args) =>
            {
                if (button.Command != null && button.Command is not ConfirmCommand)
                {
                    WrapCommandWithConfirm(button);
                }
            });
        }

        private static void WrapCommandWithConfirm(Button button)
        {
            var originalCommand = button.Command;
            if (originalCommand == null || originalCommand is ConfirmCommand)
                return;

            SetOriginalCommand(button, originalCommand);

            var message = GetConfirmMessage(button);
            var title = GetConfirmTitle(button);
            button.Command = new ConfirmCommand(originalCommand, message, title);
        }
    }
}

