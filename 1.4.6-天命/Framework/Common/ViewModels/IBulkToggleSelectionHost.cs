using TM.Framework.Common.Controls;

namespace TM.Framework.Common.ViewModels
{
    public interface IBulkToggleSelectionHost
    {
        void OnTreeNodeSelected(TreeNodeItem? node);
        void OnBusinessActivated();
    }
}
