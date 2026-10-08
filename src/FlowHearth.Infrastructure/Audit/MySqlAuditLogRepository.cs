using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Audit;
using FlowHearth.Application.Common;

namespace FlowHearth.Infrastructure.Audit;

public sealed class MySqlAuditLogRepository(IDbConnectionFactory connectionFactory)
    : IAuditLogRepository
{
    public async Task<PagedResult<AuditLogSummary>> ListAsync(
        AuditLogListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var orderBy = GetOrderBy(criteria.SortBy, criteria.SortDescending);
        var sql =
            $$"""
            SELECT a.id AS Id,a.occurred_at_utc AS OccurredAtUtc,
                   a.actor_user_id AS ActorUserId,u.username AS ActorUsername,
                   u.display_name AS ActorDisplayName,a.action AS Action,
                   a.entity_type AS EntityType,a.entity_id AS EntityId,
                   a.entity_code AS EntityCode,a.summary AS Summary,
                   a.correlation_id AS CorrelationId,
                   (a.before_json IS NOT NULL) AS HasBefore,
                   (a.after_json IS NOT NULL) AS HasAfter
            FROM audit_logs AS a
            LEFT JOIN users AS u ON u.id=a.actor_user_id
            WHERE (@EntityType IS NULL OR a.entity_type=CONVERT(@EntityType USING ascii))
              AND (@Action IS NULL OR a.action=CONVERT(@Action USING ascii))
              AND (@ActorUserId IS NULL OR a.actor_user_id=@ActorUserId)
              AND (@OccurredFromUtc IS NULL OR a.occurred_at_utc>=@OccurredFromUtc)
              AND (@OccurredToUtc IS NULL OR a.occurred_at_utc<=@OccurredToUtc)
              AND (@SearchPattern IS NULL OR a.summary LIKE @SearchPattern ESCAPE '='
                   OR a.entity_code LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(a.action USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(a.entity_type USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR u.username LIKE @SearchPattern ESCAPE '='
                   OR u.display_name LIKE @SearchPattern ESCAPE '=')
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM audit_logs AS a
            LEFT JOIN users AS u ON u.id=a.actor_user_id
            WHERE (@EntityType IS NULL OR a.entity_type=CONVERT(@EntityType USING ascii))
              AND (@Action IS NULL OR a.action=CONVERT(@Action USING ascii))
              AND (@ActorUserId IS NULL OR a.actor_user_id=@ActorUserId)
              AND (@OccurredFromUtc IS NULL OR a.occurred_at_utc>=@OccurredFromUtc)
              AND (@OccurredToUtc IS NULL OR a.occurred_at_utc<=@OccurredToUtc)
              AND (@SearchPattern IS NULL OR a.summary LIKE @SearchPattern ESCAPE '='
                   OR a.entity_code LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(a.action USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(a.entity_type USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR u.username LIKE @SearchPattern ESCAPE '='
                   OR u.display_name LIKE @SearchPattern ESCAPE '=');
            """;
        var parameters = new
        {
            criteria.EntityType,
            criteria.Action,
            criteria.ActorUserId,
            criteria.OccurredFromUtc,
            criteria.OccurredToUtc,
            SearchPattern = CreateSearchPattern(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var rows = await result.ReadAsync<AuditSummaryRow>();
        var total = await result.ReadSingleAsync<long>();
        return new PagedResult<AuditLogSummary>(
            rows.Select(row => row.ToSummary()).ToArray(),
            criteria.Page,
            criteria.PageSize,
            total);
    }

    public async Task<AuditLogDetails?> FindAsync(
        ulong auditLogId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT a.id AS Id,a.occurred_at_utc AS OccurredAtUtc,
                   a.actor_user_id AS ActorUserId,u.username AS ActorUsername,
                   u.display_name AS ActorDisplayName,a.action AS Action,
                   a.entity_type AS EntityType,a.entity_id AS EntityId,
                   a.entity_code AS EntityCode,a.summary AS Summary,
                   a.before_json AS BeforeJson,a.after_json AS AfterJson,
                   a.correlation_id AS CorrelationId
            FROM audit_logs AS a
            LEFT JOIN users AS u ON u.id=a.actor_user_id
            WHERE a.id=@AuditLogId;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AuditLogDetails>(
            new CommandDefinition(
                sql,
                new { AuditLogId = auditLogId },
                cancellationToken: cancellationToken));
    }

    private static string GetOrderBy(string sortBy, bool descending)
    {
        var column = sortBy.ToLowerInvariant() switch
        {
            "occurredat" => "a.occurred_at_utc",
            "actor" => "u.display_name",
            "action" => "a.action",
            "entitytype" => "a.entity_type",
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy)),
        };
        return $"{column} {(descending ? "DESC" : "ASC")},a.id {(descending ? "DESC" : "ASC")}";
    }

    private static string? CreateSearchPattern(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? null
            : $"%{search.Replace("=", "==", StringComparison.Ordinal).Replace("%", "=%", StringComparison.Ordinal).Replace("_", "=_", StringComparison.Ordinal)}%";

    private sealed class AuditSummaryRow
    {
        public ulong Id { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public ulong? ActorUserId { get; init; }
        public string? ActorUsername { get; init; }
        public string? ActorDisplayName { get; init; }
        public string Action { get; init; } = string.Empty;
        public string EntityType { get; init; } = string.Empty;
        public ulong EntityId { get; init; }
        public string? EntityCode { get; init; }
        public string Summary { get; init; } = string.Empty;
        public string? CorrelationId { get; init; }
        public long HasBefore { get; init; }
        public long HasAfter { get; init; }

        public AuditLogSummary ToSummary() =>
            new(
                Id,
                OccurredAtUtc,
                ActorUserId,
                ActorUsername,
                ActorDisplayName,
                Action,
                EntityType,
                EntityId,
                EntityCode,
                Summary,
                CorrelationId,
                HasBefore != 0,
                HasAfter != 0);
    }
}
