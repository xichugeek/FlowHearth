namespace FlowHearth.Application.Audit;

public sealed record AuditLogListCriteria(
    int Page,
    int PageSize,
    string? Search,
    string? EntityType,
    string? Action,
    ulong? ActorUserId,
    DateTime? OccurredFromUtc,
    DateTime? OccurredToUtc,
    string SortBy,
    bool SortDescending);

public sealed record AuditLogSummary(
    ulong Id,
    DateTime OccurredAtUtc,
    ulong? ActorUserId,
    string? ActorUsername,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    ulong EntityId,
    string? EntityCode,
    string Summary,
    string? CorrelationId,
    bool HasBefore,
    bool HasAfter);

public sealed record AuditLogDetails(
    ulong Id,
    DateTime OccurredAtUtc,
    ulong? ActorUserId,
    string? ActorUsername,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    ulong EntityId,
    string? EntityCode,
    string Summary,
    string? BeforeJson,
    string? AfterJson,
    string? CorrelationId);
