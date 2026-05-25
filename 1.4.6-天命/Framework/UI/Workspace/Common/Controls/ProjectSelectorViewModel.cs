using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ProjectSelectorViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ProjectManager _projectManager;
        private ProjectDisplayInfo? _selectedProject;

        public ProjectSelectorViewModel(ProjectManager projectManager)
        {
            _projectManager = projectManager;
            Projects = new ObservableCollection<ProjectDisplayInfo>();

            ManageProjectsCommand = new RelayCommand(OpenManageDialog);

            _projectManager.ProjectListChanged += RefreshProjects;
            _projectManager.ProjectSwitched += OnProjectSwitched;

            RefreshProjects();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region 属性

        public ObservableCollection<ProjectDisplayInfo> Projects { get; }

        public ProjectDisplayInfo? SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (_selectedProject != value)
                {
                    _selectedProject = value;
                    OnPropertyChanged();

                    if (value != null && !value.IsCurrent)
                    {
                        _ = _projectManager.SwitchProjectAsync(value.Id);
                    }
                }
            }
        }

        public string CurrentProjectName => _projectManager.CurrentProject?.Name ?? "未选择项目";

        public ICommand ManageProjectsCommand { get; }

        #endregion

        #region 方法

        private void RefreshProjects()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Projects.Clear();

                var currentId = _projectManager.CurrentProject?.Id;
                foreach (var p in _projectManager.Projects)
                {
                    Projects.Add(new ProjectDisplayInfo
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        IsCurrent = p.Id == currentId
                    });
                }

                OnPropertyChanged(nameof(CurrentProjectName));
            });
        }

        private void OnProjectSwitched(ProjectInfo project)
        {
            RefreshProjects();
            try
            {
                ServiceLocator.Get<PanelCommunicationService>().PublishRefreshChapterList();
            }
            catch { }
        }

        private void OpenManageDialog()
        {
            if (SelectedProject != null)
            {
                StandardDialog.ShowInfo("项目信息", $"当前项目：{SelectedProject.Name}");
            }
            else
            {
                StandardDialog.ShowInfo("项目信息", "当前无可用项目");
            }
        }

        #endregion
    }

    public class ProjectDisplayInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }
}
