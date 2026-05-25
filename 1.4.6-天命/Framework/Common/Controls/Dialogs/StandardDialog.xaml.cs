using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TM.Framework.Common.Controls.Dialogs
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class StandardDialog : Window
    {
        #region Win32 FlashWindowEx

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        private const uint FLASHW_STOP = 0;
        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMERNOFG = 12;

        #endregion

        public bool? Result { get; private set; }

        public StandardDialog()
        {
            InitializeComponent();
        }

        public void SetTitle(string title)
        {
            TitleText.Text = title;
        }

        public void SetContent(UIElement content)
        {
            ContentArea.Content = content;
        }

        public void SetIcon(string icon)
        {
            TitleIcon.Text = icon;
        }

        public void AddButton(string text, Action onClick, bool isPrimary = false)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 80,
                Margin = new Thickness(10, 0, 0, 0)
            };

            if (isPrimary)
            {
                button.Style = (Style)FindResource("PrimaryButtonStyle");
            }
            else
            {
                button.Style = (Style)FindResource("SecondaryButtonStyle");
            }

            button.Click += (s, e) => onClick?.Invoke();
            ButtonPanel.Children.Add(button);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public static void EnsureOwnerAndTopmost(Window dialog, Window? owner)
        {
            Window? activeWindow = null;
            Window? firstVisibleWindow = null;
            var foregroundHwnd = GetForegroundWindow();
            bool isAppInForeground = false;

            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    foreach (Window w in app.Windows)
                    {
                        if (w == dialog) continue;

                        if (firstVisibleWindow == null && w.IsVisible && w.WindowState != WindowState.Minimized)
                        {
                            firstVisibleWindow = w;
                        }

                        if (w.IsActive)
                        {
                            activeWindow = w;
                        }

                        if (!isAppInForeground)
                        {
                            try
                            {
                                var wHelper = new WindowInteropHelper(w);
                                if (wHelper.Handle == foregroundHwnd)
                                    isAppInForeground = true;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StandardDialog] 查找活跃窗口失败: {ex.Message}");
            }

            var ownerIsUsable = owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized;
            var ownerIsPreferred = ownerIsUsable && owner!.IsActive;

            var resolvedOwner = ownerIsPreferred
                ? owner
                : activeWindow ?? (ownerIsUsable ? owner : firstVisibleWindow);

            if (resolvedOwner != null)
            {
                try
                {
                    dialog.Owner = resolvedOwner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                catch
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.Topmost = true;

            if (!isAppInForeground && resolvedOwner != null)
            {
                FlashWindowForTarget(resolvedOwner);
            }
        }

        private static void FlashWindowForTarget(Window targetWindow)
        {
            try
            {
                var helper = new WindowInteropHelper(targetWindow);
                if (helper.Handle == IntPtr.Zero) return;

                var fInfo = new FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                    hwnd = helper.Handle,
                    dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                    uCount = 0,
                    dwTimeout = 0
                };
                FlashWindowEx(ref fInfo);
            }
            catch { }
        }

        public static void FlashTaskbarIfBackground(Window? targetWindow)
        {
            try
            {
                if (targetWindow == null) return;

                var helper = new WindowInteropHelper(targetWindow);
                if (helper.Handle == IntPtr.Zero) return;

                var foregroundHwnd = GetForegroundWindow();
                bool isAppInForeground = false;

                if (Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        var wHelper = new WindowInteropHelper(w);
                        if (wHelper.Handle == foregroundHwnd)
                        {
                            isAppInForeground = true;
                            break;
                        }
                    }
                }

                if (!isAppInForeground)
                {
                    var fInfo = new FLASHWINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                        hwnd = helper.Handle,
                        dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                        uCount = 0,
                        dwTimeout = 0
                    };
                    FlashWindowEx(ref fInfo);
                }
            }
            catch
            {
            }
        }

        public static bool ShowConfirm(string message, string title, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("❓");

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)))
            };

            dialog.SetContent(new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320
            });

            bool result = false;
            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { result = true; dialog.Result = true; dialog.Close(); }, true);

            dialog.ShowDialog();
            return result;
        }

        public static void ShowInfo(string message, string title, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("ℹ️");

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)))
            };

            dialog.SetContent(new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320
            });
            dialog.AddButton("确定", () => dialog.Close(), true);

            dialog.ShowDialog();
        }

        public static void ShowWarning(string message, string title, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("⚠️");

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)))
            };

            dialog.SetContent(new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320
            });
            dialog.AddButton("知道了", () => dialog.Close(), true);

            dialog.ShowDialog();
        }

        public static void ShowError(string message, string title, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("❌");

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)))
            };

            dialog.SetContent(new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320
            });
            dialog.AddButton("确定", () => dialog.Close(), true);

            dialog.ShowDialog();
        }

        public static string? ShowInput(string message, string title, string defaultValue = "", Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("✏️");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var textBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = (Brush)(dialog.TryFindResource("BorderBrush") ?? new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB))),
                BorderThickness = new Thickness(1),
                Background = (Brush)(dialog.TryFindResource("ContentBackground") ?? Brushes.White),
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))),
                Margin = new Thickness(0, 0, 0, 0)
            };

            panel.Children.Add(textBlock);
            panel.Children.Add(textBox);

            dialog.SetContent(panel);

            string? result = null;
            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { result = textBox.Text; dialog.Result = true; dialog.Close(); }, true);

            textBox.Focus();
            textBox.SelectAll();

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = textBox.Text;
                    dialog.Result = true;
                    dialog.Close();
                }
            };

            dialog.ShowDialog();
            return dialog.Result == true ? result : null;
        }

        public static string? ShowPasswordInput(string message, string title, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("🔐");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? Brushes.Black),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var passwordBox = new PasswordBox
            {
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = (Brush)(dialog.TryFindResource("BorderBrush") ?? Brushes.Gray),
                BorderThickness = new Thickness(1),
                Background = (Brush)(dialog.TryFindResource("ContentBackground") ?? Brushes.White),
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? Brushes.Black),
                MinWidth = 260
            };

            panel.Children.Add(textBlock);
            panel.Children.Add(passwordBox);

            dialog.SetContent(panel);

            string? result = null;
            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { result = passwordBox.Password; dialog.Result = true; dialog.Close(); }, true);

            passwordBox.Focus();

            passwordBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    result = passwordBox.Password;
                    dialog.Result = true;
                    dialog.Close();
                }
            };

            dialog.ShowDialog();
            return dialog.Result == true ? result : null;
        }

        public static int ShowSelection(string title, string message, System.Collections.Generic.List<string> options, Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("📋");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))),
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(textBlock);

            var listBox = new ListBox
            {
                MaxHeight = 200,
                BorderBrush = (Brush)(dialog.TryFindResource("BorderBrush") ?? new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB))),
                BorderThickness = new Thickness(1),
                Background = (Brush)(dialog.TryFindResource("ContentBackground") ?? Brushes.White)
            };
            foreach (var opt in options)
            {
                listBox.Items.Add(new ListBoxItem { Content = opt, Padding = new Thickness(8, 6, 8, 6) });
            }
            if (listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;

            panel.Children.Add(listBox);
            dialog.SetContent(panel);

            int result = -1;
            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { result = listBox.SelectedIndex; dialog.Result = true; dialog.Close(); }, true);

            listBox.MouseDoubleClick += (s, e) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    result = listBox.SelectedIndex;
                    dialog.Result = true;
                    dialog.Close();
                }
            };

            dialog.ShowDialog();
            return dialog.Result == true ? result : -1;
        }
    }
}

