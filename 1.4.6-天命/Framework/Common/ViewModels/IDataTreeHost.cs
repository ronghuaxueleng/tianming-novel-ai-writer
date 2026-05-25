using System.Windows.Input;

namespace TM.Framework.Common.ViewModels
{
    public interface IDataTreeHost
    {
        ICommand ToggleSelectedEnabledCommand { get; }

        string? AIGenerateDisabledReason { get; }
    }
}
