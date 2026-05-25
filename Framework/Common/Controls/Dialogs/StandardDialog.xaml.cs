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
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
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

        private const uint FLASHW_ALL = 3;
        private const uint FLASHW_TIMERNOFG = 12;

        #endregion

        private static readonly SolidColorBrush s_fallbackTextBrush;
        private static readonly SolidColorBrush s_fallbackBorderBrush;

        static StandardDialog()
        {
            s_fallbackTextBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
            s_fallbackTextBrush.Freeze();
            s_fallbackBorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            s_fallbackBorderBrush.Freeze();
        }

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
            TitleIcon.Source = TM.Framework.Common.Helpers.IconHelper.TryGet(icon);
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

            Window? mainWindow = null;
            try { mainWindow = Application.Current?.MainWindow; } catch { }
            bool mainWindowUsable = mainWindow != null && mainWindow != dialog
                && mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized;

            var resolvedOwner = ownerIsPreferred
                ? owner
                : (mainWindowUsable ? mainWindow : activeWindow ?? (ownerIsUsable ? owner : firstVisibleWindow));

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

        private static StandardDialog BuildMessageDialog(string message, string title, string iconKey, Window? owner)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon(iconKey);

            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 480,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush)
            };

            dialog.SetContent(new ScrollViewer
            {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320
            });

            return dialog;
        }

        public static System.Threading.Tasks.Task<bool> ShowConfirmAsync(string message, string title, Window? owner = null)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dialog = BuildMessageDialog(message, title, "Icon.HelpCircle", owner);

            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { dialog.Result = true; dialog.Close(); }, true);

            Window? resolvedOwner = dialog.Owner;
            var overlayHost = resolvedOwner as TM.Framework.Common.Helpers.UI.IModalOverlayHost;
            if (overlayHost != null)
                overlayHost.SetModalOverlay(true);
            else if (resolvedOwner != null)
                resolvedOwner.IsEnabled = false;

            dialog.Closed += (_, _) =>
            {
                if (overlayHost != null)
                    overlayHost.SetModalOverlay(false);
                else if (resolvedOwner != null)
                    resolvedOwner.IsEnabled = true;
                tcs.TrySetResult(dialog.Result == true);
            };

            dialog.Show();
            return tcs.Task;
        }

        public static bool ShowConfirm(string message, string title, Window? owner = null)
        {
            var dialog = BuildMessageDialog(message, title, "Icon.HelpCircle", owner);

            bool result = false;
            dialog.AddButton("取消", () => { dialog.Result = false; dialog.Close(); });
            dialog.AddButton("确定", () => { result = true; dialog.Result = true; dialog.Close(); }, true);

            dialog.ShowDialog();
            return result;
        }

        public static void ShowInfo(string message, string title, Window? owner = null)
        {
            var dialog = BuildMessageDialog(message, title, "Icon.Info", owner);
            dialog.AddButton("确定", () => dialog.Close(), true);
            dialog.ShowDialog();
        }

        public static void ShowWarning(string message, string title, Window? owner = null)
        {
            var dialog = BuildMessageDialog(message, title, "Icon.Warning", owner);
            dialog.AddButton("知道了", () => dialog.Close(), true);
            dialog.ShowDialog();
        }

        public static void ShowError(string message, string title, Window? owner = null)
        {
            var dialog = BuildMessageDialog(message, title, "Icon.Forbidden", owner);
            dialog.AddButton("确定", () => dialog.Close(), true);
            dialog.ShowDialog();
        }

        public static string? ShowInput(string message, string title, string defaultValue = "", Window? owner = null)
        {
            var dialog = new StandardDialog();
            EnsureOwnerAndTopmost(dialog, owner);

            dialog.SetTitle(title);
            dialog.SetIcon("Icon.Edit");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var textBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 14,
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = (Brush)(dialog.TryFindResource("BorderBrush") ?? s_fallbackBorderBrush),
                BorderThickness = new Thickness(1),
                Background = (Brush)(dialog.TryFindResource("ContentBackground") ?? Brushes.White),
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
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
            dialog.SetIcon("Icon.Lock");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var passwordBox = new PasswordBox
            {
                FontSize = 14,
                Padding = new Thickness(8, 0, 8, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
                CaretBrush = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
                MinWidth = 260,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var inputBorder = new Border
            {
                BorderBrush = (Brush)(dialog.TryFindResource("GlassBorderBrush") ?? dialog.TryFindResource("BorderBrush") ?? s_fallbackBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = (Brush)(dialog.TryFindResource("SurfaceGlass") ?? dialog.TryFindResource("ContentBackground") ?? Brushes.White),
                Height = 36,
                Child = passwordBox
            };

            panel.Children.Add(textBlock);
            panel.Children.Add(inputBorder);

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
            dialog.SetIcon("Icon.Clipboard");

            var panel = new StackPanel();

            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = (Brush)(dialog.TryFindResource("TextPrimary") ?? s_fallbackTextBrush),
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(textBlock);

            var listBox = new ListBox
            {
                MaxHeight = 200,
                BorderBrush = (Brush)(dialog.TryFindResource("BorderBrush") ?? s_fallbackBorderBrush),
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

