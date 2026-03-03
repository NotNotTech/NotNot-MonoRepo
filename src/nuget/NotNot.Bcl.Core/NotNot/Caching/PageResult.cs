namespace NotNot.Caching;

/// <summary>
/// Generic paginated result with cursor-based pagination.
/// </summary>
public record PageResult<T>(List<T> Items, int TotalCount, int? NextCursor);
