using System.Collections.ObjectModel;
using System.Reflection;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.Common.Controls.Forms
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class CommonConfigForm : UserControl
    {
        public static readonly DependencyProperty ConstraintTypesProperty =
            DependencyProperty.Register(
                nameof(ConstraintTypes),
                typeof(ObservableCollection<string>),
                typeof(CommonConfigForm),
                new PropertyMetadata(null));

        public ObservableCollection<string>? ConstraintTypes
        {
            get => (ObservableCollection<string>?)GetValue(ConstraintTypesProperty);
            set => SetValue(ConstraintTypesProperty, value);
        }

        public CommonConfigForm()
        {
            InitializeComponent();
            TM.App.Log("[标准组件] CommonConfigForm已加载");

            DataContextChanged += CommonConfigForm_DataContextChanged;

            foreach (var checkBox in GenresPanel.Children.OfType<CheckBox>())
            {
                checkBox.Checked += GenreCheckBox_Changed;
                checkBox.Unchecked += GenreCheckBox_Changed;
            }
        }

        private void CommonConfigForm_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateGenreCheckBoxes();

            if (DataContext is INovelGenresHost host)
            {
                var genres = host.NovelGenres;
                if (genres != null)
                {
                    genres.CollectionChanged -= Genres_CollectionChanged;
                    genres.CollectionChanged += Genres_CollectionChanged;
                }
            }
        }

        private void Genres_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateGenreCheckBoxes();
        }

        private void UpdateGenreCheckBoxes()
        {
            if (DataContext is not INovelGenresHost host) return;

            var genres = host.NovelGenres;
            if (genres == null) return;

            foreach (var checkBox in GenresPanel.Children.OfType<CheckBox>())
            {
                var genre = checkBox.Tag as string;
                if (genre != null)
                {
                    checkBox.Checked -= GenreCheckBox_Changed;
                    checkBox.Unchecked -= GenreCheckBox_Changed;

                    checkBox.IsChecked = genres.Contains(genre);

                    checkBox.Checked += GenreCheckBox_Changed;
                    checkBox.Unchecked += GenreCheckBox_Changed;
                }
            }
        }

        private void GenreCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is not INovelGenresHost host) return;

            var checkBox = sender as CheckBox;
            if (checkBox == null) return;

            var genre = checkBox.Tag as string;
            if (genre == null) return;

            var genres = host.NovelGenres;
            if (genres == null) return;

            if (checkBox.IsChecked == true)
            {
                if (!genres.Contains(genre))
                {
                    genres.Add(genre);
                }
            }
            else
            {
                genres.Remove(genre);
            }
        }
    }
}

