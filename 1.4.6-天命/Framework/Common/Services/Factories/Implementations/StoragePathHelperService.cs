using TM.Framework.Common.Helpers.Storage;

namespace TM.Framework.Common.Services.Factories
{
    public class StoragePathHelperService : IStoragePathHelper
    {
        public string CurrentProjectName
        {
            get => StoragePathHelper.CurrentProjectName;
            set => StoragePathHelper.CurrentProjectName = value;
        }

        public string GetStorageRoot()
        {
            return StoragePathHelper.GetStorageRoot();
        }

        public string GetProjectRoot()
        {
            return StoragePathHelper.GetProjectRoot();
        }

        public string GetCurrentProjectPath()
        {
            return StoragePathHelper.GetCurrentProjectPath();
        }

        public string GetProjectConfigPath()
        {
            return StoragePathHelper.GetProjectConfigPath();
        }

        public string GetProjectConfigPath(string moduleName)
        {
            return StoragePathHelper.GetProjectConfigPath(moduleName);
        }

        public string GetProjectGeneratedPath()
        {
            return StoragePathHelper.GetProjectGeneratedPath();
        }

        public string GetProjectChaptersPath()
        {
            return StoragePathHelper.GetProjectChaptersPath();
        }

        public string GetProjectCategoriesPath()
        {
            return StoragePathHelper.GetProjectCategoriesPath();
        }

        public string GetProjectValidationPath()
        {
            return StoragePathHelper.GetProjectValidationPath();
        }

        public string GetProjectHistoryPath()
        {
            return StoragePathHelper.GetProjectHistoryPath();
        }

        public string[] GetAllProjects()
        {
            return StoragePathHelper.GetAllProjects();
        }

        public bool CreateProject(string projectName)
        {
            return StoragePathHelper.CreateProject(projectName);
        }

        public string GetFrameworkPath(string subPath)
        {
            return StoragePathHelper.GetFrameworkPath(subPath);
        }

        public string GetFrameworkStoragePath(string subPath)
        {
            return StoragePathHelper.GetFrameworkStoragePath(subPath);
        }

        public string GetServicesStoragePath(string subPath)
        {
            return StoragePathHelper.GetServicesStoragePath(subPath);
        }

        public string GetModulesStoragePath(string subPath)
        {
            return StoragePathHelper.GetModulesStoragePath(subPath);
        }

        public string GetFilePath(string layer, string subPath, string fileName)
        {
            return StoragePathHelper.GetFilePath(layer, subPath, fileName);
        }

        public void EnsureDirectoryExists(string path)
        {
            StoragePathHelper.EnsureDirectoryExists(path);
        }

        public void ClearCache()
        {
            StoragePathHelper.ClearCache();
        }
    }
}
