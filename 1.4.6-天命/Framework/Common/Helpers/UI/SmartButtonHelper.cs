using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class SmartButtonHelper
    {
        public static readonly DependencyProperty EnableSmartStyleProperty =
            DependencyProperty.RegisterAttached(
                "EnableSmartStyle",
                typeof(bool),
                typeof(SmartButtonHelper),
                new PropertyMetadata(false, OnEnableSmartStyleChanged));

        public static bool GetEnableSmartStyle(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableSmartStyleProperty);
        }

        public static void SetEnableSmartStyle(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableSmartStyleProperty, value);
        }

        private static void OnEnableSmartStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Button button && (bool)e.NewValue)
            {
                button.Loaded += Button_Loaded;
                button.Unloaded += Button_Unloaded;
            }
        }

        private static void Button_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                ApplySmartStyle(button);

                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    ContentControl.ContentProperty, typeof(Button));
                descriptor?.AddValueChanged(button, OnContentChanged);
            }
        }

        private static void Button_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    ContentControl.ContentProperty, typeof(Button));
                descriptor?.RemoveValueChanged(button, OnContentChanged);
            }
        }

        private static void OnContentChanged(object? sender, System.EventArgs e)
        {
            if (sender is Button button)
            {
                ApplySmartStyle(button);
            }
        }

        private static void ApplySmartStyle(Button button)
        {
            if (button.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue)
            {
                return;
            }

            string content = GetButtonText(button);
            Style? targetStyle = DetermineStyle(button, content);

            if (targetStyle != null)
            {
                button.Style = targetStyle;
            }
        }

        private static string GetButtonText(Button button)
        {
            if (button.Content == null)
                return string.Empty;

            if (button.Content is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                    {
                        if (textBlock.Text.Length > 2)
                        {
                            return textBlock.Text;
                        }
                    }
                }
            }

            return button.Content.ToString() ?? string.Empty;
        }

        private static Style? DetermineStyle(Button button, string text)
        {
            string[] primaryKeywords = { "确定", "保存", "提交", "应用", "创建", "添加", "新建", "登录", "注册" };

            string[] dangerKeywords = { "删除", "移除", "清空", "重置", "注销", "卸载" };

            string[] secondaryKeywords = { "取消", "返回", "关闭", "刷新", "查看", "编辑", "导出", "导入" };

            foreach (var keyword in dangerKeywords)
            {
                if (text.Contains(keyword))
                {
                    return FindStyle(button, "DangerButtonStyle");
                }
            }

            foreach (var keyword in primaryKeywords)
            {
                if (text.Contains(keyword))
                {
                    return FindStyle(button, "PrimaryButtonStyle");
                }
            }

            foreach (var keyword in secondaryKeywords)
            {
                if (text.Contains(keyword))
                {
                    return FindStyle(button, "SecondaryButtonStyle");
                }
            }

            return FindStyle(button, "SecondaryButtonStyle");
        }

        private static Style? FindStyle(FrameworkElement element, string styleKey)
        {
            return element.TryFindResource(styleKey) as Style;
        }
    }
}

