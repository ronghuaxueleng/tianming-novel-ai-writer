using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.Common.Helpers
{
    public class ItemsSourceTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? FlatTemplate { get; set; }

        public DataTemplate? TreeTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is IEnumerable enumerable)
            {
                var enumerator = enumerable.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    var firstItem = enumerator.Current;
                    if (firstItem != null)
                    {
                        if (firstItem is TM.Framework.Common.Controls.TreeNodeItem)
                        {
                            TM.App.Log($"[ItemsSourceTemplateSelector] 检测到TreeNodeItem，使用树形模板");
                            return TreeTemplate;
                        }
                    }
                }
            }

            return FlatTemplate;
        }
    }
}

