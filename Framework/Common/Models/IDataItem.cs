namespace TM.Framework.Common.Models
{
    public interface IDataItem : IEnableable
    {
        string Id { get; set; }

        string Name { get; set; }

        string Category { get; set; }

        string CategoryId { get; set; }
    }
}
