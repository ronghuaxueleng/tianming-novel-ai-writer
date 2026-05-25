namespace TM.Framework.Common.Services.Factories
{
    public interface IStoragePathHelper
    {
        string CurrentProjectName { get; set; }

        string GetStorageRoot();

        string GetProjectRoot();

        string GetCurrentProjectPath();

        string GetProjectConfigPath();

        string GetProjectConfigPath(string moduleName);

        string GetProjectGeneratedPath();

        string GetProjectChaptersPath();

        string GetProjectCategoriesPath();

        string GetProjectValidationPath();

        string GetProjectHistoryPath();

        string[] GetAllProjects();

        bool CreateProject(string projectName);

        string GetFrameworkPath(string subPath);

        string GetFrameworkStoragePath(string subPath);

        string GetServicesStoragePath(string subPath);

        string GetModulesStoragePath(string subPath);

        string GetFilePath(string layer, string subPath, string fileName);

        void EnsureDirectoryExists(string path);

        void ClearCache();
    }
}
