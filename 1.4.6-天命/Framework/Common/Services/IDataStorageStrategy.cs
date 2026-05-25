using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public interface IDataStorageStrategy<TData>
    {
        List<TData> Load();
        Task<List<TData>> LoadAsync();
        void Save(List<TData> items);
        Task SaveAsync(List<TData> items);
    }
}
