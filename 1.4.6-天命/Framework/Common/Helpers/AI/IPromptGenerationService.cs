using System.Threading.Tasks;

namespace TM.Framework.Common.Helpers.AI;

public interface IPromptGenerationService
{
    Task<PromptGenerationResult> GenerateModulePromptAsync(PromptGenerationContext context);
}
