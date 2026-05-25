using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.EntityManager.Interfaces
{
    public interface IEntityManagerService
    {
        Task<EntityResult<T>> CreateEntityAsync<T>(T entity) where T : BaseEntity;

        Task<EntityResult<T>> GetEntityAsync<T>(string entityId) where T : BaseEntity;

        Task<EntityResult<T>> UpdateEntityAsync<T>(T entity) where T : BaseEntity;

        Task<EntityResult<bool>> DeleteEntityAsync(string entityId, EntityType entityType);

        Task<EntityQueryResult<T>> QueryEntitiesAsync<T>(EntityQuery query) where T : BaseEntity;

        Task<EntityResult<EntityRelation>> CreateRelationAsync(EntityRelation relation);

        Task<EntityQueryResult<EntityRelation>> GetRelationsAsync(string entityId, RelationQuery? query = null);

        Task<ReferenceCheckResult> CheckReferenceIntegrityAsync(string projectId);

        event EventHandler<EntityChangedEventArgs> EntityChanged;
    }

    public abstract class BaseEntity
    {
        public string Id { get; set; } = ShortIdGenerator.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public EntityType Type { get; set; }
        public string ProjectId { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    public enum EntityType
    {
        Character,
        Location,
        Item,
        Organization,
        Concept,
        Event,
        Custom
    }

    public class EntityResult<T>
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public List<ValidationError> ValidationErrors { get; set; } = new();
    }

    public class EntityQueryResult<T>
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public PaginationInfo Pagination { get; set; } = new();
    }

    public class EntityQuery
    {
        public EntityType? EntityType { get; set; }
        public string ProjectId { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, object> Filters { get; set; } = new();
        public string SearchText { get; set; } = string.Empty;
        public PaginationInfo Pagination { get; set; } = new();
        public List<SortCriteria> Sorting { get; set; } = new();
    }

    public class EntityRelation
    {
        public string Id { get; set; } = ShortIdGenerator.NewGuid().ToString();
        public string SourceEntityId { get; set; } = string.Empty;
        public string TargetEntityId { get; set; } = string.Empty;
        public RelationType RelationType { get; set; }
        public string RelationName { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    public enum RelationType
    {
        OneToOne,
        OneToMany,
        ManyToMany,
        Dependency,
        Association,
        Composition,
        Inheritance
    }

    public class RelationQuery
    {
        public RelationType? RelationType { get; set; }
        public string RelationName { get; set; } = string.Empty;
        public bool IncludeInactive { get; set; } = false;
        public int MaxDepth { get; set; } = 1;
    }

    public class ReferenceCheckResult
    {
        public bool IsValid { get; set; }
        public List<ReferenceIssue> Issues { get; set; } = new();
        public int TotalReferences { get; set; }
        public int ValidReferences { get; set; }
        public int BrokenReferences { get; set; }
    }

    public class ReferenceIssue
    {
        public string IssueId { get; set; } = ShortIdGenerator.NewGuid().ToString();
        public ReferenceIssueType IssueType { get; set; }
        public string SourceEntityId { get; set; } = string.Empty;
        public string TargetEntityId { get; set; } = string.Empty;
        public string FieldPath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
    }

    public enum ReferenceIssueType
    {
        MissingTarget,
        TypeMismatch,
        CircularReference,
        OrphanEntity,
        DuplicateReference
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class ValidationError
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object? AttemptedValue { get; set; }
    }

    public class PaginationInfo
    {
        public int PageIndex { get; set; } = 0;
        public int PageSize { get; set; } = 20;
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }

    public class SortCriteria
    {
        public string Field { get; set; } = string.Empty;
        public SortDirection Direction { get; set; } = SortDirection.Ascending;
    }

    public enum SortDirection
    {
        Ascending,
        Descending
    }

    public class EntityChangedEventArgs : EventArgs
    {
        public string EntityId { get; set; } = string.Empty;
        public EntityType EntityType { get; set; }
        public EntityChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public Dictionary<string, object> ChangeDetails { get; set; } = new();
    }

    public enum EntityChangeType
    {
        Created,
        Updated,
        Deleted,
        RelationAdded,
        RelationRemoved,
        RelationUpdated
    }
}
