namespace TM.Framework.Common.Services
{
    public interface IAsyncInitializable
    {
        System.Threading.Tasks.Task InitializeAsync();
    }
}
