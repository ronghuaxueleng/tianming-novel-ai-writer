using System.Threading.Tasks;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public class NullBookWebSearchProvider : IBookWebSearchProvider
    {
        public Task<BookWebSearchResult> SearchAsync(string query, int timeoutSeconds = 10)
        {
            return Task.FromResult(new BookWebSearchResult
            {
                Success = false,
                Summary = string.Empty,
                ErrorMessage = "Search API 未配置"
            });
        }
    }
}
