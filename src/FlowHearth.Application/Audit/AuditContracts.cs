using FlowHearth.Application.Common;

namespace FlowHearth.Application.Audit;

public interface IAuditLogRepository
{
    Task<PagedResult<AuditLogSummary>> ListAsync(
        AuditLogListCriteria criteria,
        CancellationToken cancellationToken);

    Task<AuditLogDetails?> FindAsync(
        ulong auditLogId,
        CancellationToken cancellationToken);
}

public interface IAuditLogService
{
    Task<PagedResult<AuditLogSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? entityType,
        string? action,
        ulong? actorUserId,
        DateTime? occurredFromUtc,
        DateTime? occurredToUtc,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);

    Task<AuditLogDetails> GetAsync(
        ulong auditLogId,
        CancellationToken cancellationToken);
}
