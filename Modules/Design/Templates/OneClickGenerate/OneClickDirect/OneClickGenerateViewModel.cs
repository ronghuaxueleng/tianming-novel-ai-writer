using System.ComponentModel;
using System.Reflection;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
    }
}
