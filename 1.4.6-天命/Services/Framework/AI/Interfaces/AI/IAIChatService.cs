using System;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Interfaces.AI
{
    public interface IAIChatService
    {
        Task<string> SendMessageAsync(string displayText, string promptForModel);

        Task<string> SendMessageAsync(string displayText, string promptForModel, CancellationToken cancellationToken);

        Task<string> SendStreamMessageAsync(string displayText, string promptForModel, Action<string> onChunk, CancellationToken cancellationToken = default);

        void CancelCurrentRequest();
    }
}
