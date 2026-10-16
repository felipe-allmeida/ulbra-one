namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Project (project) entities (wire form <c>project_{guid:N}</c>).</summary>
[PrefixedId("project_")]
public readonly partial record struct ProjectId(Guid Value);
