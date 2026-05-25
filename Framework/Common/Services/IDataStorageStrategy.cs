using System.Collections.Generic;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public interface IDataStorageStrategy<TData>
    {
        Task<List<TData>> LoadAsync();
        Task SaveAsync(List<TData> items);
    }
}
