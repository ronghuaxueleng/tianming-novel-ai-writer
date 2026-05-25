using System.Collections.ObjectModel;

namespace TM.Framework.Common.ViewModels
{
    public interface INovelGenresHost
    {
        ObservableCollection<string> NovelGenres { get; }
    }
}
