using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TM.Framework.Common.Controls.Menus
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class FileOperationsContextMenu : ContextMenu
    {
        public FileOperationsContextMenu()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty RefreshCommandProperty =
            DependencyProperty.Register(nameof(RefreshCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty CreateFileCommandProperty =
            DependencyProperty.Register(nameof(CreateFileCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty CreateFolderCommandProperty =
            DependencyProperty.Register(nameof(CreateFolderCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty RevealInExplorerCommandProperty =
            DependencyProperty.Register(nameof(RevealInExplorerCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty CopyCommandProperty =
            DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty PasteCommandProperty =
            DependencyProperty.Register(nameof(PasteCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty RenameCommandProperty =
            DependencyProperty.Register(nameof(RenameCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public static readonly DependencyProperty DeleteCommandProperty =
            DependencyProperty.Register(nameof(DeleteCommand), typeof(ICommand), typeof(FileOperationsContextMenu));

        public ICommand? RefreshCommand
        {
            get => (ICommand?)GetValue(RefreshCommandProperty);
            set => SetValue(RefreshCommandProperty, value);
        }

        public ICommand? CreateFileCommand
        {
            get => (ICommand?)GetValue(CreateFileCommandProperty);
            set => SetValue(CreateFileCommandProperty, value);
        }

        public ICommand? CreateFolderCommand
        {
            get => (ICommand?)GetValue(CreateFolderCommandProperty);
            set => SetValue(CreateFolderCommandProperty, value);
        }

        public ICommand? RevealInExplorerCommand
        {
            get => (ICommand?)GetValue(RevealInExplorerCommandProperty);
            set => SetValue(RevealInExplorerCommandProperty, value);
        }

        public ICommand? CopyCommand
        {
            get => (ICommand?)GetValue(CopyCommandProperty);
            set => SetValue(CopyCommandProperty, value);
        }

        public ICommand? PasteCommand
        {
            get => (ICommand?)GetValue(PasteCommandProperty);
            set => SetValue(PasteCommandProperty, value);
        }

        public ICommand? RenameCommand
        {
            get => (ICommand?)GetValue(RenameCommandProperty);
            set => SetValue(RenameCommandProperty, value);
        }

        public ICommand? DeleteCommand
        {
            get => (ICommand?)GetValue(DeleteCommandProperty);
            set => SetValue(DeleteCommandProperty, value);
        }

        protected override void OnOpened(RoutedEventArgs e)
        {
            base.OnOpened(e);

            if (DataContext != null)
            {
                return;
            }

            if (PlacementTarget is FrameworkElement fe)
            {
                DataContext = fe.DataContext;
            }
        }
    }
}

