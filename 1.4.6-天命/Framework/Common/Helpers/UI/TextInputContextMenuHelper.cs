using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TM.Framework.Common.Helpers.UI
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class TextInputContextMenuHelper
    {
        public static readonly DependencyProperty EnableStandardEditMenuProperty =
            DependencyProperty.RegisterAttached(
                "EnableStandardEditMenu",
                typeof(bool),
                typeof(TextInputContextMenuHelper),
                new PropertyMetadata(false, OnEnableStandardEditMenuChanged));

        public static void SetEnableStandardEditMenu(DependencyObject element, bool value)
            => element.SetValue(EnableStandardEditMenuProperty, value);

        public static bool GetEnableStandardEditMenu(DependencyObject element)
            => (bool)element.GetValue(EnableStandardEditMenuProperty);

        private static void OnEnableStandardEditMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not bool enabled || !enabled)
            {
                return;
            }

            if (d is TextBoxBase textBoxBase)
            {
                AttachStandardEditMenu(textBoxBase);
                return;
            }

            if (d is Control control)
            {
                AttachStandardEditMenu(control);
            }
        }

        private static void AttachStandardEditMenu(Control control)
        {
            if (control == null)
            {
                return;
            }

            var menu = new ContextMenu();

            menu.Template = BuildStandardTemplate();
            menu.HasDropShadow = false;
            menu.Background = Brushes.Transparent;
            menu.BorderThickness = new Thickness(0);

            menu.Items.Add(new MenuItem
            {
                Header = "全选",
                Command = ApplicationCommands.SelectAll,
                CommandTarget = control
            });

            menu.Items.Add(new MenuItem
            {
                Header = "复制",
                Command = ApplicationCommands.Copy,
                CommandTarget = control
            });

            menu.Items.Add(new MenuItem
            {
                Header = "粘贴",
                Command = ApplicationCommands.Paste,
                CommandTarget = control
            });

            menu.Items.Add(new MenuItem
            {
                Header = "剪切",
                Command = ApplicationCommands.Cut,
                CommandTarget = control
            });

            control.ContextMenu = menu;
        }

        private static ControlTemplate BuildStandardTemplate()
        {
            var template = new ControlTemplate(typeof(ContextMenu));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetResourceReference(Border.BackgroundProperty, "ContentBackground");
            borderFactory.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var scrollFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scrollFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

            var itemsPresenterFactory = new FrameworkElementFactory(typeof(ItemsPresenter));

            scrollFactory.AppendChild(itemsPresenterFactory);
            borderFactory.AppendChild(scrollFactory);

            template.VisualTree = borderFactory;

            return template;
        }
    }
}
