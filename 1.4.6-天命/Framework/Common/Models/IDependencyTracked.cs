using System.Collections.Generic;

namespace TM.Framework.Common.Models
{
    public interface IDependencyTracked
    {
        string Id { get; set; }

        Dictionary<string, int> DependencyModuleVersions { get; set; }
    }
}
