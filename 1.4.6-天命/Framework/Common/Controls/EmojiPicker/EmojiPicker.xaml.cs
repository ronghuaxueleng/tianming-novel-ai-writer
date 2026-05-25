using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TM.Framework.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class EmojiPicker : UserControl
    {
        public EmojiPicker()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty SelectedEmojiProperty =
            DependencyProperty.Register(nameof(SelectedEmoji), typeof(string), typeof(EmojiPicker),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string SelectedEmoji
        {
            get => (string)GetValue(SelectedEmojiProperty);
            set => SetValue(SelectedEmojiProperty, value);
        }
    }
}

