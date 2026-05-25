using System;
using System.Threading.Tasks;

namespace TM.Framework.User.Account.Login.Bootstrap
{
    public class BootstrapTask : IBootstrapTask
    {
        private readonly Func<Task> _executeAction;

        public string Name { get; }
        public string Description { get; }

        public BootstrapTask(string name, string description, Func<Task> executeAction)
        {
            Name = name;
            Description = description;
            _executeAction = executeAction;
        }

        public async Task ExecuteAsync()
        {
            await _executeAction();
        }
    }
}
