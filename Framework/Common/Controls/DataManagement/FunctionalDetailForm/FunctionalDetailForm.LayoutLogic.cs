using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.Common.Controls.DataManagement
{
    public partial class FunctionalDetailForm : System.Windows.Controls.UserControl
    {
        #region Layout Logic

        private const double FieldSpacing = 12d;
        private double _lastKnownAvailableWidth;

        private readonly List<RowLayoutContext> _rowContexts = new();

        private void FunctionalDetailForm_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleLayoutRefresh();
        }

        private void FunctionalDetailForm_Unloaded(object sender, RoutedEventArgs e)
        {
            ClearRowContexts();
        }

        private static void LayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FunctionalDetailForm form)
            {
                form.ScheduleLayoutRefresh();
            }
        }

        private void ScheduleLayoutRefresh()
        {
            if (_layoutPending)
            {
                return;
            }

            if (!IsLoaded)
            {
                Loaded -= DeferredLoadedHandler;
                Loaded += DeferredLoadedHandler;
                return;
            }

            _layoutPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _layoutPending = false;
                RefreshLayout();
            }));
        }

        private void DeferredLoadedHandler(object sender, RoutedEventArgs e)
        {
            Loaded -= DeferredLoadedHandler;
            ScheduleLayoutRefresh();
        }

        private void RefreshLayout()
        {
            try
            {
                RebuildFieldRows();
                UpdateAllRowLayouts();
            }
            catch (Exception ex)
            {
                App.Log($"[FunctionalDetailForm] 布局刷新失败: {ex}");
                throw;
            }
        }

        private void RebuildFieldRows()
        {
            ClearRowContexts();
            FieldRowsPanel.Children.Clear();

            var fields = BuildEffectiveFieldList();
            if (fields.Count == 0)
            {
                return;
            }

            var availableWidth = GetAvailableRowWidth();
            var groupContexts = GroupFields(fields);

            _lastKnownAvailableWidth = availableWidth;

            foreach (var group in groupContexts.OrderBy(g => g.Order))
            {
                var rowSegments = SplitFieldsIntoRows(group, availableWidth).ToList();
                foreach (var segment in rowSegments)
                {
                    var (rowGrid, rowContext) = BuildRowGrid(segment);
                    FieldRowsPanel.Children.Add(rowGrid);
                    _rowContexts.Add(rowContext);
                }
            }

            ApplyRowSpacing();
        }

        private List<FunctionalDetailField> BuildEffectiveFieldList()
        {
            var result = new List<FunctionalDetailField>();

            if (Fields is { Count: > 0 })
            {
                foreach (var field in Fields)
                {
                    if (field?.Content is UIElement)
                    {
                        result.Add(field);
                    }
                }
            }

            if (result.Count > 0)
            {
                return result;
            }

            return ShowBasicFields ? BuildBasicFields() : new List<FunctionalDetailField>();
        }

        private List<FunctionalDetailField> BuildBasicFields()
        {
            var fields = new List<FunctionalDetailField>();

            var basicTotalWidth = GetBasicTotalWidth();
            var basicNameWidth = GetBasicNameWidth(basicTotalWidth);

            var nameField = new FunctionalDetailField
            {
                Label = NameFieldLabel,
                GroupKey = "BasicRow1",
                Order = 0,
                MinWidth = basicNameWidth,
                MaxWidth = basicNameWidth,
                AllowGrow = NameAllowGrow,
                GrowWeight = NameGrowWeight,
                Content = CreateBasicNameContent()
            };
            fields.Add(nameField);

            if (ShowOccupyField)
            {
                var occupyField = new FunctionalDetailField
                {
                    Label = OccupyFieldLabel,
                    GroupKey = "BasicRow1",
                    Order = 1,
                    MinWidth = OccupyMinWidth,
                    MaxWidth = OccupyMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    Content = CreateBasicOccupyContent()
                };
                fields.Add(occupyField);
            }

            if (ShowIconField)
            {
                var iconField = new FunctionalDetailField
                {
                    Label = IconFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 0,
                    MinWidth = IconMinWidth,
                    MaxWidth = IconMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicIconContent()
                };
                fields.Add(iconField);
            }

            if (ShowStatusField)
            {
                var statusField = new FunctionalDetailField
                {
                    Label = StatusFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 1,
                    MinWidth = StatusMinWidth,
                    MaxWidth = StatusMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicStatusContent()
                };
                fields.Add(statusField);
            }

            if (ShowTypeField)
            {
                var typeField = new FunctionalDetailField
                {
                    Label = TypeFieldLabel,
                    GroupKey = "BasicRow2",
                    Order = 2,
                    MinWidth = TypeMinWidth,
                    MaxWidth = TypeMaxWidth,
                    AllowGrow = false,
                    GrowWeight = 0d,
                    AllowMerge = true,
                    Content = CreateBasicTypeContent()
                };
                fields.Add(typeField);
            }

            if (ShowCategoryField)
            {
                var categoryField = new FunctionalDetailField
                {
                    Label = CategoryFieldLabel,
                    GroupKey = "BasicCategory",
                    MinWidth = basicTotalWidth,
                    MaxWidth = basicTotalWidth,
                    AllowGrow = CategoryAllowGrow,
                    GrowWeight = CategoryGrowWeight,
                    AllowMerge = false,
                    Order = 100,
                    Content = CreateBasicCategoryContent()
                };
                fields.Add(categoryField);
            }

            return fields;
        }

        private double GetBasicTotalWidth()
        {
            return IconMinWidth + StatusMinWidth + TypeMinWidth + (FieldSpacing * 2d);
        }

        private double GetBasicNameWidth(double totalWidth)
        {
            var nameWidth = totalWidth - OccupyMinWidth - FieldSpacing;
            return Math.Max(0d, nameWidth);
        }

        private FrameworkElement CreateBasicNameContent()
        {
            if (NameFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            TM.Framework.Common.Helpers.UI.TextInputContextMenuHelper.SetEnableStandardEditMenu(textBox, true);

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(NameValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            textBox.SetBinding(TextBox.IsReadOnlyProperty, new System.Windows.Data.Binding(nameof(IsNameReadOnly))
            {
                Source = this
            });

            return textBox;
        }

        private FrameworkElement CreateBasicOccupyContent()
        {
            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsReadOnly = true
            };

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(OccupyValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return textBox;
        }

        private FrameworkElement CreateBasicStatusContent()
        {
            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("StandardComboBoxStyle") is Style comboStyle)
            {
                comboBox.Style = comboStyle;
            }

            comboBox.Items.Add("已禁用");
            comboBox.Items.Add("已启用");

            comboBox.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(StatusValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return comboBox;
        }

        private FrameworkElement CreateBasicIconContent()
        {
            try
            {
                var emojiPicker = new TM.Framework.Common.Controls.EmojiPicker
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                emojiPicker.SetBinding(TM.Framework.Common.Controls.EmojiPicker.SelectedEmojiProperty,
                    new System.Windows.Data.Binding(nameof(IconValue))
                    {
                        Source = this,
                        Mode = System.Windows.Data.BindingMode.TwoWay,
                        UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                    });

                return emojiPicker;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FunctionalDetailForm] EmojiPicker创建失败: {ex.Message}");
            }

            var textBox = new TextBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            TM.Framework.Common.Helpers.UI.TextInputContextMenuHelper.SetEnableStandardEditMenu(textBox, true);

            if (TryFindResource("StandardTextBoxStyle") is Style textBoxStyle)
            {
                textBox.Style = textBoxStyle;
            }

            textBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(IconValue))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            return textBox;
        }

        private FrameworkElement CreateBasicCategoryContent()
        {
            if (CategoryFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("TreeComboBoxStyle") is Style treeComboStyle)
            {
                comboBox.Style = treeComboStyle;
            }

            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(CategoryItemsSource))
            {
                Source = this
            });

            comboBox.SetBinding(ComboBox.IsDropDownOpenProperty, new System.Windows.Data.Binding(nameof(CategoryIsDropDownOpen))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.SelectedPathProperty,
                new System.Windows.Data.Binding(nameof(CategorySelectedPath))
                {
                    Source = this,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.DisplayIconProperty,
                new System.Windows.Data.Binding(nameof(CategoryDisplayIcon))
                {
                    Source = this,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.MaxLevelProperty,
                new System.Windows.Data.Binding(nameof(CategoryMaxLevel))
                {
                    Source = this
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.NodeDoubleClickCommandProperty,
                new System.Windows.Data.Binding(nameof(CategoryNodeSelectCommand))
                {
                    Source = this
                });

            comboBox.SetBinding(TM.Framework.Common.Helpers.UI.ComboBoxHelper.SelectOnDoubleClickOnlyProperty,
                new System.Windows.Data.Binding(nameof(CategorySelectOnDoubleClickOnly))
                {
                    Source = this
                });

            return comboBox;
        }

        private FrameworkElement CreateBasicTypeContent()
        {
            if (TypeFieldContent is FrameworkElement customElement)
            {
                EnsureDetached(customElement);
                customElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                return customElement;
            }

            var comboBox = new ComboBox
            {
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (TryFindResource("StandardComboBoxStyle") is Style comboStyle)
            {
                comboBox.Style = comboStyle;
            }

            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(TypeItemsSource))
            {
                Source = this
            });

            comboBox.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding(nameof(TypeSelectedItem))
            {
                Source = this,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            comboBox.SetBinding(ComboBox.IsEditableProperty, new System.Windows.Data.Binding(nameof(IsTypeEditable))
            {
                Source = this
            });

            return comboBox;
        }

        private IList<GroupContext> GroupFields(IReadOnlyList<FunctionalDetailField> fields)
        {
            var groups = new List<GroupContext>();
            var groupMap = new Dictionary<string, GroupContext>();
            int sequence = 0;

            foreach (var field in fields)
            {
                if (field.Content is not UIElement)
                {
                    continue;
                }

                string key;
                if (!field.AllowMerge || string.IsNullOrWhiteSpace(field.GroupKey))
                {
                    key = $"__single_{ShortIdGenerator.NewGuid()}";
                }
                else
                {
                    key = field.GroupKey!;
                }

                if (!groupMap.TryGetValue(key, out var group))
                {
                    group = new GroupContext(key, field.Order, sequence);
                    groupMap[key] = group;
                    groups.Add(group);
                }

                group.Fields.Add(new FieldWrapper(field, sequence));
                sequence++;
            }

            return groups;
        }

        private double GetAvailableRowWidth()
        {
            double width = FieldRowsPanel.ActualWidth;
            if (double.IsNaN(width) || width <= 0)
            {
                width = FieldRowsPanel.RenderSize.Width;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = ActualWidth;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = RenderSize.Width;
            }

            if (double.IsNaN(width) || width <= 0)
            {
                width = 800d;
            }

            return Math.Max(200d, width - 1d);
        }

        private IEnumerable<IReadOnlyList<FieldWrapper>> SplitFieldsIntoRows(GroupContext group, double maxRowWidth)
        {
            var rows = new List<List<FieldWrapper>>();
            var currentRow = new List<FieldWrapper>();
            double currentWidth = 0;

            bool isForcedSingleRow = group.Key == "BasicRow1" || group.Key == "BasicRow2";

            foreach (var fieldWrapper in group.Fields)
            {
                var field = fieldWrapper.Field;
                double fieldWidth = Math.Max(1d, field.MinWidth);
                double spacing = currentRow.Count > 0 ? FieldSpacing : 0;

                if (!isForcedSingleRow && currentRow.Count > 0 && currentWidth + spacing + fieldWidth > maxRowWidth)
                {
                    rows.Add(currentRow);
                    currentRow = new List<FieldWrapper>();
                    currentWidth = 0;
                    spacing = 0;
                }

                currentRow.Add(fieldWrapper);
                currentWidth += spacing + fieldWidth;
            }

            if (currentRow.Count > 0)
            {
                rows.Add(currentRow);
            }

            return rows;
        }

        private (Grid Grid, RowLayoutContext Context) BuildRowGrid(IReadOnlyList<FieldWrapper> fields)
        {
            var rowGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };

            var rowContext = new RowLayoutContext(rowGrid);
            int columnIndex = 0;

            for (int i = 0; i < fields.Count; i++)
            {
                var fieldWrapper = fields[i];
                var field = fieldWrapper.Field;

                var column = new ColumnDefinition
                {
                    Width = new GridLength(field.MinWidth),
                    MinWidth = Math.Max(0, field.MinWidth),
                    MaxWidth = field.MaxWidth > 0 ? Math.Max(field.MinWidth, field.MaxWidth) : double.PositiveInfinity
                };

                rowGrid.ColumnDefinitions.Add(column);
                rowContext.FieldColumns.Add(new FieldColumnContext(column, field));

                var container = CreateFieldContainer(field);
                Grid.SetColumn(container, columnIndex);
                rowGrid.Children.Add(container);

                columnIndex++;

                if (i < fields.Count - 1)
                {
                    var spacingColumn = new ColumnDefinition
                    {
                        Width = new GridLength(FieldSpacing),
                        MinWidth = FieldSpacing,
                        MaxWidth = FieldSpacing
                    };
                    rowGrid.ColumnDefinitions.Add(spacingColumn);
                    rowContext.SpacingColumns.Add(spacingColumn);
                    columnIndex++;
                }
            }

            rowGrid.SizeChanged += RowGrid_SizeChanged;
            rowGrid.Tag = rowContext;

            return (rowGrid, rowContext);
        }

        private void ApplyRowSpacing()
        {
            int count = FieldRowsPanel.Children.Count;
            for (int i = 0; i < count; i++)
            {
                if (FieldRowsPanel.Children[i] is FrameworkElement fe)
                {
                    fe.Margin = new Thickness(0, 0, 0, i == count - 1 ? 0 : 10);
                }
            }
        }

        private FrameworkElement CreateFieldContainer(FunctionalDetailField field)
        {
            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            bool hasLabel = !string.IsNullOrWhiteSpace(field.Label);

            if (hasLabel)
            {
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = field.Label,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                container.Children.Add(label);
            }
            else
            {
                container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            if (field.Content is UIElement element)
            {
                EnsureDetached(element);

                if (element is FrameworkElement fe)
                {
                    fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                var presenter = new ContentPresenter
                {
                    Content = element,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                Grid.SetRow(presenter, hasLabel ? 1 : 0);
                container.Children.Add(presenter);
            }

            return container;
        }

        private void UpdateAllRowLayouts()
        {
            foreach (var context in _rowContexts)
            {
                UpdateRowLayout(context);
            }
        }

        private void RowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is RowLayoutContext context)
            {
                UpdateRowLayout(context);

                var availableWidth = GetAvailableRowWidth();
                if (Math.Abs(availableWidth - _lastKnownAvailableWidth) > 20)
                {
                    _lastKnownAvailableWidth = availableWidth;
                    ScheduleLayoutRefresh();
                }
            }
        }

        private void UpdateRowLayout(RowLayoutContext context)
        {
            if (context.FieldColumns.Count == 0)
            {
                return;
            }

            double available = context.Grid.ActualWidth - context.Grid.Margin.Left - context.Grid.Margin.Right;
            if (available <= 0)
            {
                return;
            }

            double spacingWidth = FieldSpacing * Math.Max(0, context.FieldColumns.Count - 1);
            available -= spacingWidth;
            if (available <= 0)
            {
                return;
            }

            var minWidths = context.FieldColumns.Select(c => c.Field.MinWidth).ToArray();
            var maxWidths = context.FieldColumns.Select(c => c.Field.MaxWidth > 0 ? Math.Max(c.Field.MinWidth, c.Field.MaxWidth) : double.PositiveInfinity).ToArray();
            var growWeights = context.FieldColumns.Select(c => c.Field.AllowGrow ? Math.Max(0, c.Field.GrowWeight) : 0).ToArray();

            double minTotal = minWidths.Sum();
            var targetWidths = (double[])minWidths.Clone();

            if (available > minTotal)
            {
                double leftover = available - minTotal;
                var adjustable = new List<int>();
                for (int i = 0; i < growWeights.Length; i++)
                {
                    if (growWeights[i] > 0 && maxWidths[i] > minWidths[i])
                    {
                        adjustable.Add(i);
                    }
                }

                while (leftover > 0.1 && adjustable.Count > 0)
                {
                    double weightSum = adjustable.Sum(i => growWeights[i]);
                    if (weightSum <= 0)
                    {
                        break;
                    }

                    double consumed = 0;
                    foreach (var index in adjustable.ToList())
                    {
                        double capacity = maxWidths[index] - targetWidths[index];
                        if (capacity <= 0)
                        {
                            adjustable.Remove(index);
                            continue;
                        }

                        double share = leftover * (growWeights[index] / weightSum);
                        double addition = double.IsInfinity(maxWidths[index])
                            ? share
                            : Math.Min(share, capacity);

                        targetWidths[index] += addition;
                        consumed += addition;

                        if (!double.IsInfinity(maxWidths[index]) && targetWidths[index] >= maxWidths[index] - 0.1)
                        {
                            adjustable.Remove(index);
                        }
                    }

                    if (consumed <= 0.1)
                    {
                        break;
                    }

                    leftover -= consumed;
                }
            }

            for (int i = 0; i < context.FieldColumns.Count; i++)
            {
                double width = Math.Max(minWidths[i], targetWidths[i]);
                width = Math.Min(width, maxWidths[i]);

                var column = context.FieldColumns[i].Column;
                column.MinWidth = minWidths[i];
                column.MaxWidth = double.IsInfinity(maxWidths[i]) ? double.PositiveInfinity : maxWidths[i];
                column.Width = new GridLength(width, GridUnitType.Pixel);
            }
        }

        private void ClearRowContexts()
        {
            foreach (var context in _rowContexts)
            {
                context.Grid.SizeChanged -= RowGrid_SizeChanged;
                context.Grid.Tag = null;
            }

            _rowContexts.Clear();
        }

        private static void EnsureDetached(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            if (VisualTreeHelper.GetParent(element) is DependencyObject parent)
            {
                switch (parent)
                {
                    case Panel panel:
                        panel.Children.Remove(element);
                        break;
                    case ContentPresenter presenter:
                        presenter.Content = null;
                        break;
                    case ContentControl control:
                        control.Content = null;
                        break;
                    case Decorator decorator:
                        decorator.Child = null;
                        break;
                }
            }
        }

        private sealed class RowLayoutContext
        {
            public RowLayoutContext(Grid grid)
            {
                Grid = grid;
            }

            public Grid Grid { get; }
            public List<FieldColumnContext> FieldColumns { get; } = new();
            public List<ColumnDefinition> SpacingColumns { get; } = new();
        }

        private sealed class FieldColumnContext
        {
            public FieldColumnContext(ColumnDefinition column, FunctionalDetailField field)
            {
                Column = column;
                Field = field;
            }

            public ColumnDefinition Column { get; }
            public FunctionalDetailField Field { get; }
        }

        private sealed class GroupContext
        {
            public GroupContext(string key, int order, int sequence)
            {
                Key = key;
                Order = order == 0 ? sequence : order;
            }

            public string Key { get; }
            public int Order { get; }
            public List<FieldWrapper> Fields { get; } = new();
        }

        private sealed class FieldWrapper
        {
            public FieldWrapper(FunctionalDetailField field, int sequence)
            {
                Field = field;
                Sequence = sequence;
            }

            public FunctionalDetailField Field { get; }
            public int Sequence { get; }
        }

        #endregion
    }
}
