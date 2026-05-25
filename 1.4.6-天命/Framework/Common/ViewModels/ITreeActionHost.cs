using System.Windows.Input;

namespace TM.Framework.Common.ViewModels
{
    public interface ITreeActionHost
    {
        ICommand TreeAfterActionCommand { get; }
    }
}

