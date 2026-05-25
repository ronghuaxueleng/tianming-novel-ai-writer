using System.Reflection;

namespace TM.Framework.UI.Workspace.RightPanel.Modes
{
    [Obfuscation(Exclude = true)]
    public enum ChatMode
    {
        Channel = 0,

        Agent = 1,

        Plan = 2,

        Edit = 3,

        Business = 4
    }
}
