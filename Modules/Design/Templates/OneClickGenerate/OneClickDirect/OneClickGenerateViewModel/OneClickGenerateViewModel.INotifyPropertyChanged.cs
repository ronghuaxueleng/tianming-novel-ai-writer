using System.ComponentModel;
using System.Runtime.CompilerServices;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }
}
