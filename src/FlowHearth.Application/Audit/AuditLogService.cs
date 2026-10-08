using FlowHearth.Application.Common;

namespace FlowHearth.Application.Audit;

public sealed class AuditLogService(IAuditLogRepository repository) : IAuditLogService
{
    private static readonly HashSet<string> SortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "occurredAt",
            "actor",
            "action",
            "entityType",
        };

    public Task<PagedResult<AuditLogSummary>> ListAsync(
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
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于 0。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1-100。");
        }

        if (occurredFromUtc.HasValue
            && occurredToUtc.HasValue
            && occurredFromUtc > occurredToUtc)
        {
            throw FlowHearthValidationException.For("occurredToUtc", "结束时间不能早于开始时间。");
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "occurredAt" : sortBy.Trim();
        if (!SortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new AuditLogListCriteria(
                page,
                pageSize,
                NormalizeOptional(search, 100, "search"),
                NormalizeOptional(entityType, 64, "entityType"),
                NormalizeOptional(action, 64, "action"),
                actorUserId,
                occurredFromUtc,
                occurredToUtc,
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<AuditLogDetails> GetAsync(
        ulong auditLogId,
        CancellationToken cancellationToken)
    {
        if (auditLogId == 0)
        {
            throw FlowHearthValidationException.For("auditLogId", "审计标识必须大于 0。");
        }

        return await repository.FindAsync(auditLogId, cancellationToken)
            ?? throw new NotFoundException("审计记录不存在。");
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(field, $"{field} 不能超过 {maximumLength} 个字符。");
        }

        return normalized;
    }
}
