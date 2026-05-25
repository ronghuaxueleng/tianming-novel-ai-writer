using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TM.Framework.Common.Behaviors
{
    public static class DragDropBehavior
    {
        private static Point _startPoint;
        private static bool _isDragging;
        private static object? _draggedItem;
        private static FrameworkElement? _draggedElement;
        private static int _draggedIndex;

        #region IsEnabled 附加属性

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(DragDropBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        #endregion

        #region DropCommand 附加属性

        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.RegisterAttached(
                "DropCommand",
                typeof(ICommand),
                typeof(DragDropBehavior),
                new PropertyMetadata(null));

        public static ICommand GetDropCommand(DependencyObject obj) => (ICommand)obj.GetValue(DropCommandProperty);
        public static void SetDropCommand(DependencyObject obj, ICommand value) => obj.SetValue(DropCommandProperty, value);

        #endregion

        #region DropIndicatorBrush 附加属性

        public static readonly DependencyProperty DropIndicatorBrushProperty =
            DependencyProperty.RegisterAttached(
                "DropIndicatorBrush",
                typeof(Brush),
                typeof(DragDropBehavior),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(59, 130, 246))));

        public static Brush GetDropIndicatorBrush(DependencyObject obj) => (Brush)obj.GetValue(DropIndicatorBrushProperty);
        public static void SetDropIndicatorBrush(DependencyObject obj, Brush value) => obj.SetValue(DropIndicatorBrushProperty, value);

        #endregion

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ItemsControl itemsControl)
                return;

            if ((bool)e.NewValue)
            {
                itemsControl.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
                itemsControl.PreviewMouseMove += OnPreviewMouseMove;
                itemsControl.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
                itemsControl.DragOver += OnDragOver;
                itemsControl.Drop += OnDrop;
                itemsControl.AllowDrop = true;
            }
            else
            {
                itemsControl.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
                itemsControl.PreviewMouseMove -= OnPreviewMouseMove;
                itemsControl.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
                itemsControl.DragOver -= OnDragOver;
                itemsControl.Drop -= OnDrop;
            }
        }

        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = false;

            if (sender is ItemsControl itemsControl)
            {
                var element = e.OriginalSource as DependencyObject;
                while (element != null && element != itemsControl)
                {
                    if (element is FrameworkElement fe && 
                        itemsControl.ItemContainerGenerator.IndexFromContainer(fe) >= 0)
                    {
                        _draggedElement = fe;
                        _draggedIndex = itemsControl.ItemContainerGenerator.IndexFromContainer(fe);
                        _draggedItem = itemsControl.Items[_draggedIndex];
                        break;
                    }

                    var parent = VisualTreeHelper.GetParent(element);
                    if (parent is ContentPresenter cp)
                    {
                        var container = itemsControl.ItemContainerGenerator.ContainerFromItem(cp.Content);
                        if (container is FrameworkElement containerFe)
                        {
                            _draggedElement = containerFe;
                            _draggedIndex = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
                            _draggedItem = cp.Content;
                            break;
                        }
                    }
                    element = parent;
                }
            }
        }

        private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
                return;

            var currentPoint = e.GetPosition(null);
            var diff = _startPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (!_isDragging && sender is ItemsControl itemsControl)
                {
                    _isDragging = true;

                    var data = new DataObject("DragDropItem", _draggedItem);
                    data.SetData("SourceIndex", _draggedIndex);

                    DragDrop.DoDragDrop(itemsControl, data, DragDropEffects.Move);

                    _isDragging = false;
                    _draggedItem = null;
                    _draggedElement = null;
                }
            }
        }

        private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _draggedItem = null;
            _draggedElement = null;
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("DragDropItem"))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private static void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("DragDropItem") || sender is not ItemsControl itemsControl)
                return;

            var droppedData = e.Data.GetData("DragDropItem");
            var sourceIndex = (int)e.Data.GetData("SourceIndex");

            var targetIndex = GetDropTargetIndex(itemsControl, e.GetPosition(itemsControl));

            if (targetIndex < 0)
                targetIndex = itemsControl.Items.Count;

            var command = GetDropCommand(itemsControl);
            if (command != null && command.CanExecute(null))
            {
                var args = new DropEventArgs(sourceIndex, targetIndex);
                command.Execute(args);
            }

            e.Handled = true;
        }

        private static int GetDropTargetIndex(ItemsControl itemsControl, Point dropPosition)
        {
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                var containerPosition = container.TransformToAncestor(itemsControl).Transform(new Point(0, 0));
                var containerHeight = container.ActualHeight;

                if (dropPosition.Y < containerPosition.Y + containerHeight / 2)
                {
                    return i;
                }
            }

            return itemsControl.Items.Count;
        }
    }

    public class DropEventArgs
    {
        public int SourceIndex { get; }
        public int TargetIndex { get; }

        public DropEventArgs(int sourceIndex, int targetIndex)
        {
            SourceIndex = sourceIndex;
            TargetIndex = targetIndex;
        }
    }
}
