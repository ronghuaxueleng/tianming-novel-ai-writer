using System;
using System.Threading.Tasks;

namespace TM.Framework.User.Account.Login.Bootstrap
{
    public interface IBootstrapTask
    {
        string Name { get; }

        string Description { get; }

        Task ExecuteAsync();
    }

    public class BootstrapProgressEventArgs : EventArgs
    {
        public int CurrentTaskIndex { get; set; }

        public int CompletedTasks { get; set; }

        public int TotalTasks { get; set; }

        public string CurrentTaskName { get; set; } = string.Empty;

        public string CurrentTaskDescription { get; set; } = string.Empty;

        public double ProgressPercentage => TotalTasks > 0 ? (double)CompletedTasks / TotalTasks * 100 : 0;

        public bool IsCompleted => TotalTasks > 0 && CompletedTasks >= TotalTasks;
    }
}
