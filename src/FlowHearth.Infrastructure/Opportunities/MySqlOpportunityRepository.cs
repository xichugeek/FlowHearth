using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Opportunities;
using FlowHearth.Domain.Projects;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Opportunities;

public sealed class MySqlOpportunityRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null)
    : IOpportunityRepository
{
    public async Task<PagedResult<OpportunitySummary>> ListAsync(
        OpportunityListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var archiveClause = criteria.ArchiveMode switch
        {
            OpportunityArchiveMode.Active => "o.archived_at_utc IS NULL",
            OpportunityArchiveMode.Archived => "o.archived_at_utc IS NOT NULL",
            OpportunityArchiveMode.All => "1 = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var orderBy = criteria.SortBy.ToLowerInvariant() switch
        {
            "code" => "o.opportunity_code",
            "title" => "o.title",
            "stage" => "o.stage",
            "expectedamount" => "o.expected_amount",
            "probability" => "o.probability_percent",
            "expectedclosedate" => "o.expected_close_date",
            "updatedat" => "o.updated_at_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var direction = criteria.SortDescending ? "DESC" : "ASC";
        var sql = $"""
            SELECT COUNT(*)
            FROM opportunities AS o
            INNER JOIN customers AS c ON c.id = o.customer_id
            WHERE {archiveClause}
              AND (@Stage IS NULL OR o.stage = @Stage)
              AND (@CustomerId IS NULL OR o.customer_id = @CustomerId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(o.opportunity_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR o.title LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '=');

            SELECT o.id AS Id, o.opportunity_code AS Code,
                   o.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   c.name AS CustomerName, o.title AS Title, o.stage AS Stage,
                   o.expected_amount AS ExpectedAmount,
                   o.probability_percent AS ProbabilityPercent,
                   o.expected_close_date AS ExpectedCloseDate,
                   o.archived_at_utc AS ArchivedAtUtc,
                   p.id AS ProjectId, p.project_code AS ProjectCode,
                   p.name AS ProjectName, p.status AS ProjectStatus,
                   o.version AS Version, o.updated_at_utc AS UpdatedAtUtc
            FROM opportunities AS o
            INNER JOIN customers AS c ON c.id = o.customer_id
            LEFT JOIN projects AS p ON p.source_opportunity_id = o.id
            WHERE {archiveClause}
              AND (@Stage IS NULL OR o.stage = @Stage)
              AND (@CustomerId IS NULL OR o.customer_id = @CustomerId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(o.opportunity_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR o.title LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '=')
            ORDER BY {orderBy} {direction}, o.id {direction}
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new
        {
            Stage = criteria.Stage?.ToString(),
            criteria.CustomerId,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<OpportunityRow>())
            .Select(row => row.ToSummary())
            .ToArray();
        return new PagedResult<OpportunitySummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<OpportunityDetails?> GetAsync(
        ulong opportunityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, opportunityId, false, cancellationToken);
    }

    public async Task<OpportunityDetails> CreateAsync(
        CreateOpportunityData data,
        CancellationToken cancellationToken)
    {
        const string sequenceSql =
            """
            INSERT INTO number_sequences (sequence_name, current_value, updated_at_utc)
            VALUES (@SequenceName, LAST_INSERT_ID(1), @NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value = LAST_INSERT_ID(current_value + 1),
                updated_at_utc = VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO opportunities
                (opportunity_code, customer_id, title, stage, expected_amount,
                 probability_percent, expected_close_date, description,
                 version, created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @CustomerId, @Title, @Stage, @ExpectedAmount,
                 @ProbabilityPercent, @ExpectedCloseDate, @Description,
                 1, @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await RequireActiveCustomerAsync(connection, transaction, data.CustomerId, cancellationToken);
        var sequence = await connection.QuerySingleAsync<ulong>(
            Command(
                sequenceSql,
                new { SequenceName = $"opportunity:{data.NowUtc.Year:D4}", data.NowUtc },
                transaction,
                cancellationToken));
        var opportunityPrefix = runtimeSettingsProvider is null
            ? OpportunityCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Opportunity;
        var code = OpportunityCode.Format(
            opportunityPrefix,
            data.NowUtc.Year,
            sequence);
        var opportunityId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    Code = code,
                    data.CustomerId,
                    data.Title,
                    Stage = data.Stage.ToString(),
                    data.ExpectedAmount,
                    data.ProbabilityPercent,
                    data.ExpectedCloseDate,
                    data.Description,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "opportunity.created",
            "opportunity",
            opportunityId,
            code,
            $"为客户 {customer.Name} 创建商机 {data.Title}",
            null,
            new { data.CustomerId, data.Title, stage = data.Stage.ToString(), data.ExpectedAmount, data.ProbabilityPercent },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(opportunityId, cancellationToken)
            ?? throw new InvalidOperationException("Created opportunity could not be loaded.");
    }

    public async Task<OpportunityDetails?> UpdateAsync(
        ulong opportunityId,
        UpdateOpportunityData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE opportunities
            SET customer_id = @CustomerId,
                title = @Title,
                expected_amount = @ExpectedAmount,
                probability_percent = @ProbabilityPercent,
                expected_close_date = @ExpectedCloseDate,
                description = @Description,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @OpportunityId
              AND version = @Version
              AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, opportunityId, true, cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        _ = await RequireActiveCustomerAsync(connection, transaction, data.CustomerId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    OpportunityId = opportunityId,
                    data.CustomerId,
                    data.Title,
                    data.ExpectedAmount,
                    data.ProbabilityPercent,
                    data.ExpectedCloseDate,
                    data.Description,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "opportunity.updated",
            "opportunity",
            opportunityId,
            before.Code,
            $"更新商机 {data.Title}",
            before,
            new { data.CustomerId, data.Title, data.ExpectedAmount, data.ProbabilityPercent, data.ExpectedCloseDate, data.Description },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(opportunityId, cancellationToken);
    }

    public async Task<OpportunityDetails?> TransitionAsync(
        ulong opportunityId,
        TransitionOpportunityData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE opportunities
            SET stage = @Stage,
                probability_percent = @ProbabilityPercent,
                lost_reason = @LostReason,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @OpportunityId
              AND version = @Version
              AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, opportunityId, true, cancellationToken);
        if (before is null
            || !Enum.TryParse<OpportunityStage>(before.Stage, true, out var currentStage)
            || !OpportunityStagePolicy.CanTransition(currentStage, data.Stage))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var probability = data.Stage switch
        {
            OpportunityStage.Won => 100,
            OpportunityStage.Lost => 0,
            _ => before.ProbabilityPercent,
        };
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    OpportunityId = opportunityId,
                    Stage = data.Stage.ToString(),
                    ProbabilityPercent = probability,
                    data.LostReason,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "opportunity.stage_changed",
            "opportunity",
            opportunityId,
            before.Code,
            $"商机 {before.Title} 从 {currentStage} 转为 {data.Stage}",
            new { stage = currentStage.ToString(), before.ProbabilityPercent, before.LostReason },
            new { stage = data.Stage.ToString(), ProbabilityPercent = probability, data.LostReason },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(opportunityId, cancellationToken);
    }

    public async Task<OpportunityDetails?> SetArchivedAsync(
        ulong opportunityId,
        SetOpportunityArchiveData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE opportunities
            SET archived_at_utc = CASE WHEN @Archived = 1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id = CASE WHEN @Archived = 1 THEN @ActorUserId ELSE NULL END,
                version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @OpportunityId
              AND version = @Version
              AND ((@Archived = 1 AND archived_at_utc IS NULL)
                   OR (@Archived = 0 AND archived_at_utc IS NOT NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, opportunityId, true, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    OpportunityId = opportunityId,
                    data.Archived,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            data.Archived ? "opportunity.archived" : "opportunity.restored",
            "opportunity",
            opportunityId,
            before?.Code,
            data.Archived ? $"归档商机 {before?.Title}" : $"恢复商机 {before?.Title}",
            new { archived = !data.Archived },
            new { archived = data.Archived },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(opportunityId, cancellationToken);
    }

    public async Task<ProjectReference?> ConvertToProjectAsync(
        ulong opportunityId,
        ConvertOpportunityData data,
        CancellationToken cancellationToken)
    {
        const string sequenceSql =
            """
            INSERT INTO number_sequences (sequence_name, current_value, updated_at_utc)
            VALUES (@SequenceName, LAST_INSERT_ID(1), @NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value = LAST_INSERT_ID(current_value + 1),
                updated_at_utc = VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO projects
                (project_code, customer_id, source_opportunity_id, name, status,
                 contract_amount, progress_percent, version,
                 created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @CustomerId, @OpportunityId, @Name, @Status,
                 @ContractAmount, 0.00, 1,
                 @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        const string touchOpportunitySql =
            """
            UPDATE opportunities
            SET version = version + 1,
                updated_at_utc = @NowUtc,
                updated_by_user_id = @ActorUserId
            WHERE id = @OpportunityId AND version = @Version;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var opportunity = await GetSnapshotAsync(connection, transaction, opportunityId, true, cancellationToken);
        if (opportunity is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var existing = await GetProjectAsync(connection, transaction, opportunityId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        if (opportunity.Version != data.Version
            || opportunity.ArchivedAtUtc.HasValue
            || !string.Equals(opportunity.Stage, OpportunityStage.Won.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var sequence = await connection.QuerySingleAsync<ulong>(
            Command(
                sequenceSql,
                new { SequenceName = $"project:{data.NowUtc.Year:D4}", data.NowUtc },
                transaction,
                cancellationToken));
        var projectPrefix = runtimeSettingsProvider is null
            ? ProjectCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Project;
        var projectCode = ProjectCode.Format(
            projectPrefix,
            data.NowUtc.Year,
            sequence);
        var projectId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    Code = projectCode,
                    opportunity.CustomerId,
                    OpportunityId = opportunityId,
                    Name = opportunity.Title,
                    Status = ProjectStatus.Planning.ToString(),
                    ContractAmount = opportunity.ExpectedAmount,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        var touched = await connection.ExecuteAsync(
            Command(
                touchOpportunitySql,
                new { OpportunityId = opportunityId, data.Version, data.NowUtc, data.ActorUserId },
                transaction,
                cancellationToken));
        if (touched == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var project = new ProjectReference(
            projectId,
            projectCode,
            opportunity.Title,
            ProjectStatus.Planning.ToString());
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "project.created_from_opportunity",
            "project",
            projectId,
            projectCode,
            $"由商机 {opportunity.Code} 创建项目 {opportunity.Title}",
            null,
            new { opportunityId, opportunity.CustomerId, opportunity.ExpectedAmount },
            data.NowUtc,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "opportunity.converted_to_project",
            "opportunity",
            opportunityId,
            opportunity.Code,
            $"商机 {opportunity.Title} 转为项目 {projectCode}",
            null,
            project,
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return project;
    }

    private static async Task<OpportunityDetails?> GetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong opportunityId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT o.id AS Id, o.opportunity_code AS Code,
                   o.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   c.name AS CustomerName, o.title AS Title, o.stage AS Stage,
                   o.expected_amount AS ExpectedAmount,
                   o.probability_percent AS ProbabilityPercent,
                   o.expected_close_date AS ExpectedCloseDate,
                   o.description AS Description, o.lost_reason AS LostReason,
                   o.archived_at_utc AS ArchivedAtUtc,
                   p.id AS ProjectId, p.project_code AS ProjectCode,
                   p.name AS ProjectName, p.status AS ProjectStatus,
                   o.version AS Version, o.created_at_utc AS CreatedAtUtc,
                   o.updated_at_utc AS UpdatedAtUtc
            FROM opportunities AS o
            INNER JOIN customers AS c ON c.id = o.customer_id
            LEFT JOIN projects AS p ON p.source_opportunity_id = o.id
            WHERE o.id = @OpportunityId
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var row = await connection.QuerySingleOrDefaultAsync<OpportunityRow>(
            Command(sql, new { OpportunityId = opportunityId }, transaction, cancellationToken));
        return row?.ToDetails();
    }

    private static Task<OpportunitySnapshot?> GetSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong opportunityId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT id AS Id, opportunity_code AS Code, customer_id AS CustomerId,
                   title AS Title, stage AS Stage, expected_amount AS ExpectedAmount,
                   probability_percent AS ProbabilityPercent,
                   expected_close_date AS ExpectedCloseDate,
                   description AS Description, lost_reason AS LostReason,
                   archived_at_utc AS ArchivedAtUtc, version AS Version
            FROM opportunities
            WHERE id = @OpportunityId
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        return connection.QuerySingleOrDefaultAsync<OpportunitySnapshot>(
            Command(sql, new { OpportunityId = opportunityId }, transaction, cancellationToken));
    }

    private static async Task<CustomerReference> RequireActiveCustomerAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id, customer_code AS Code, name AS Name
            FROM customers
            WHERE id = @CustomerId AND archived_at_utc IS NULL
            FOR UPDATE;
            """;
        return await connection.QuerySingleOrDefaultAsync<CustomerReference>(
                Command(sql, new { CustomerId = customerId }, transaction, cancellationToken))
            ?? throw new NotFoundException("客户不存在或已经归档。");
    }

    private static Task<ProjectReference?> GetProjectAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong opportunityId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id, project_code AS Code, name AS Name, status AS Status
            FROM projects
            WHERE source_opportunity_id = @OpportunityId;
            """;
        return connection.QuerySingleOrDefaultAsync<ProjectReference>(
            Command(sql, new { OpportunityId = opportunityId }, transaction, cancellationToken));
    }

    private static Task<int> WriteAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        string entityType,
        ulong entityId,
        string? entityCode,
        string summary,
        object? before,
        object? after,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO audit_logs
                (occurred_at_utc, actor_user_id, action, entity_type,
                 entity_id, entity_code, summary, before_json, after_json)
            VALUES
                (@NowUtc, @ActorUserId, @Action, @EntityType,
                 @EntityId, @EntityCode, @Summary, @BeforeJson, @AfterJson);
            """;
        return connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    EntityCode = entityCode,
                    Summary = summary,
                    BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
                    AfterJson = after is null ? null : JsonSerializer.Serialize(after),
                },
                transaction,
                cancellationToken));
    }

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private sealed class OpportunityRow
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string CustomerName { get; init; }
        public required string Title { get; init; }
        public required string Stage { get; init; }
        public decimal ExpectedAmount { get; init; }
        public int ProbabilityPercent { get; init; }
        public DateTime? ExpectedCloseDate { get; init; }
        public string? Description { get; init; }
        public string? LostReason { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong? ProjectId { get; init; }
        public string? ProjectCode { get; init; }
        public string? ProjectName { get; init; }
        public string? ProjectStatus { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public OpportunitySummary ToSummary() =>
            new(
                Id,
                Code,
                CustomerId,
                CustomerCode,
                CustomerName,
                Title,
                Enum.Parse<OpportunityStage>(Stage, true),
                ExpectedAmount,
                ProbabilityPercent,
                ExpectedCloseDate,
                ArchivedAtUtc.HasValue,
                ToProject(),
                Version,
                UpdatedAtUtc);

        public OpportunityDetails ToDetails() =>
            new(
                Id,
                Code,
                CustomerId,
                CustomerCode,
                CustomerName,
                Title,
                Enum.Parse<OpportunityStage>(Stage, true),
                ExpectedAmount,
                ProbabilityPercent,
                ExpectedCloseDate,
                Description,
                LostReason,
                ArchivedAtUtc.HasValue,
                ArchivedAtUtc,
                ToProject(),
                Version,
                CreatedAtUtc,
                UpdatedAtUtc);

        private ProjectReference? ToProject() =>
            ProjectId.HasValue && ProjectCode is not null && ProjectName is not null && ProjectStatus is not null
                ? new ProjectReference(ProjectId.Value, ProjectCode, ProjectName, ProjectStatus)
                : null;
    }

    private sealed class OpportunitySnapshot
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string Title { get; init; }
        public required string Stage { get; init; }
        public decimal ExpectedAmount { get; init; }
        public int ProbabilityPercent { get; init; }
        public DateTime? ExpectedCloseDate { get; init; }
        public string? Description { get; init; }
        public string? LostReason { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class CustomerReference
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
    }
}
