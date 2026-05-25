using System.Threading.Tasks;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public interface IBookWebSearchProvider
    {
        Task<BookWebSearchResult> SearchAsync(string query, int timeoutSeconds = 10);
    }

    public class BookWebSearchResult
    {
        public bool Success { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
