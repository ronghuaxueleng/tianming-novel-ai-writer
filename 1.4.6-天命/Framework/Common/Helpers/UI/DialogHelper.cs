using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TM.Framework.Common.Controls.Dialogs;

namespace TM.Framework.Common.Helpers.UI
{
    public static class DialogHelper
    {
        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
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

            System.Diagnostics.Debug.WriteLine($"[DialogHelper] {key}: {ex.Message}");
        }

        private static void AttachFollowOwnerCenter(StandardDialog dialog)
        {
            var owner = dialog.Owner;
            if (owner == null)
            {
                return;
            }

            void Recenter()
            {
                if (!dialog.IsVisible)
                {
                    return;
                }

                var width = dialog.ActualWidth > 0 ? dialog.ActualWidth : dialog.Width;
                var height = dialog.ActualHeight > 0 ? dialog.ActualHeight : dialog.Height;

                if (double.IsNaN(width) || width <= 0 || double.IsNaN(height) || height <= 0)
                {
                    return;
                }

                dialog.Left = owner.Left + (owner.ActualWidth - width) / 2;
                dialog.Top = owner.Top + (owner.ActualHeight - height) / 2;
            }

            void OwnerChanged(object? _, EventArgs __) => Recenter();

            owner.LocationChanged += OwnerChanged;
            owner.SizeChanged += OwnerChanged;

            dialog.Loaded += (_, __) => Recenter();
            dialog.SizeChanged += (_, __) => Recenter();
            dialog.Closed += (_, __) =>
            {
                owner.LocationChanged -= OwnerChanged;
                owner.SizeChanged -= OwnerChanged;
            };
        }

        public static bool ShowFormDialog<T>(
            string title,
            string icon,
            T form,
            Func<T, bool>? onConfirm = null,
            string confirmText = "确定",
            string cancelText = "取消",
            Window? owner = null) where T : UIElement
        {
            var dialog = new StandardDialog();
            StandardDialog.EnsureOwnerAndTopmost(dialog, owner);

            AttachFollowOwnerCenter(dialog);

            dialog.SetTitle(title);
            dialog.SetIcon(icon);
            dialog.SetContent(form);

            bool confirmed = false;

            dialog.AddButton(cancelText, () => dialog.Close());

            dialog.AddButton(confirmText, () =>
            {
                if (onConfirm != null)
                {
                    if (onConfirm(form))
                    {
                        confirmed = true;
                        dialog.Close();
                    }
                }
                else
                {
                    confirmed = true;
                    dialog.Close();
                }
            }, true);

            dialog.ShowDialog();
            return confirmed;
        }

        public static bool ShowCustomDialog(
            string title,
            string icon,
            UIElement content,
            Action? onConfirm = null,
            string confirmText = "确定",
            string cancelText = "取消",
            Window? owner = null)
        {
            var dialog = new StandardDialog();
            StandardDialog.EnsureOwnerAndTopmost(dialog, owner);

            AttachFollowOwnerCenter(dialog);

            dialog.SetTitle(title);
            dialog.SetIcon(icon);
            dialog.SetContent(content);

            bool confirmed = false;

            dialog.AddButton(cancelText, () => dialog.Close());
            dialog.AddButton(confirmText, () =>
            {
                confirmed = true;
                onConfirm?.Invoke();
                dialog.Close();
            }, true);

            dialog.ShowDialog();
            return confirmed;
        }
    }
}

