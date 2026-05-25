namespace TM.Framework.Common.Models
{
    public interface ICategory : IEnableable
    {
        string Id { get; set; }

        string Name { get; }

        string Icon { get; }

        string? ParentCategory { get; }

        int Level { get; }

        int Order { get; set; }

        bool IsBuiltIn { get; set; }
    }
}

