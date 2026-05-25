using System.Reflection;
using System.Windows.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Profile.BasicInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class BasicInfoView : UserControl
    {
        public BasicInfoView()
        {
            InitializeComponent();
            DataContext = ServiceLocator.Get<BasicInfoViewModel>();
            TM.App.Log("[BasicInfoView] 基本信息界面已初始化");
        }
    }
}
