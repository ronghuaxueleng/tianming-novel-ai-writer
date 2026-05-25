using System.Reflection;
using System.Windows.Controls;

namespace TM.Modules.Generate.Elements.Blueprint
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class BlueprintView : UserControl
    {
        public BlueprintView(BlueprintViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Unloaded += (_, _) =>
            {
                if (DataContext is System.IDisposable disposable)
                    disposable.Dispose();
            };
        }
    }
}
