using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Projects;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Projects;

public sealed class MySqlProjectRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null)
    : IProjectRepository
{
    public async Task<PagedResult<ProjectSummary>> ListAsync(
        ProjectListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var archiveClause = criteria.ArchiveMode switch
        {
            ProjectArchiveMode.Active => "p.archived_at_utc IS NULL",
            ProjectArchiveMode.Archived => "p.archived_at_utc IS NOT NULL",
            ProjectArchiveMode.All => "1 = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var orderBy = criteria.SortBy.ToLowerInvariant() switch
        {
            "code" => "p.project_code",
            "name" => "p.name",
            "status" => "p.status",
            "contractamount" => "p.contract_amount",
            "progress" => "p.progress_percent",
            "plannedenddate" => "p.planned_end_date",
            "updatedat" => "p.updated_at_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var direction = criteria.SortDescending ? "DESC" : "ASC";
        var sql = $"""
            SELECT COUNT(*)
            FROM projects AS p
            INNER JOIN customers AS c ON c.id = p.customer_id
            LEFT JOIN opportunities AS o ON o.id = p.source_opportunity_id
            WHERE {archiveClause}
              AND (@Status IS NULL OR p.status = @Status)
              AND (@CustomerId IS NULL OR p.customer_id = @CustomerId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR p.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(o.opportunity_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=');

            SELECT p.id AS Id, p.project_code AS Code,
                   p.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   c.name AS CustomerName, p.name AS Name, p.status AS Status,
                   p.contract_amount AS ContractAmount,
                   p.progress_percent AS ProgressPercent,
                   p.planned_end_date AS PlannedEndDate,
                   p.archived_at_utc AS ArchivedAtUtc,
                   o.opportunity_code AS SourceOpportunityCode,
                   (SELECT COUNT(*) FROM project_members pm
                    WHERE pm.project_id = p.id AND pm.deleted_at_utc IS NULL) AS MemberCount,
                   (SELECT COUNT(*) FROM project_milestones mm
                    WHERE mm.project_id = p.id AND mm.deleted_at_utc IS NULL) AS MilestoneCount,
                   p.version AS Version, p.updated_at_utc AS UpdatedAtUtc
            FROM projects AS p
            INNER JOIN customers AS c ON c.id = p.customer_id
            LEFT JOIN opportunities AS o ON o.id = p.source_opportunity_id
            WHERE {archiveClause}
              AND (@Status IS NULL OR p.status = @Status)
              AND (@CustomerId IS NULL OR p.customer_id = @CustomerId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR p.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(o.opportunity_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=')
            ORDER BY {orderBy} {direction}, p.id {direction}
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new
        {
            Status = criteria.Status?.ToString(),
            criteria.CustomerId,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(Command(sql, parameters, null, cancellationToken));
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<ProjectSummaryRow>()).Select(row => row.ToDetails()).ToArray();
        return new PagedResult<ProjectSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<ProjectDetails?> GetAsync(ulong projectId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS UserId, username AS Username, display_name AS DisplayName, email AS Email
            FROM users
            WHERE is_active = 1 AND deleted_at_utc IS NULL
            ORDER BY display_name, id;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<ProjectMemberCandidate>(
            Command(sql, null, null, cancellationToken))).AsList();
    }

    public async Task<ProjectDetails> CreateAsync(CreateProjectData data, CancellationToken cancellationToken)
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
                 contract_amount, progress_percent, description,
                 planned_start_date, planned_end_date, version,
                 created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @CustomerId, NULL, @Name, @Status,
                 @ContractAmount, @ProgressPercent, @Description,
                 @PlannedStartDate, @PlannedEndDate, 1,
                 @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var customer = await RequireActiveCustomerAsync(connection, transaction, data.CustomerId, cancellationToken);
        var sequence = await connection.QuerySingleAsync<ulong>(
            Command(sequenceSql, new { SequenceName = $"project:{data.NowUtc.Year:D4}", data.NowUtc }, transaction, cancellationToken));
        var prefix = runtimeSettingsProvider is null
            ? ProjectCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Project;
        var code = ProjectCode.Format(prefix, data.NowUtc.Year, sequence);
        var projectId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    Code = code,
                    data.CustomerId,
                    data.Name,
                    Status = ProjectStatus.Planning.ToString(),
                    data.ContractAmount,
                    data.ProgressPercent,
                    data.Description,
                    data.PlannedStartDate,
                    data.PlannedEndDate,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project.created", "project", projectId, code, $"为客户 {customer.Name} 创建项目 {data.Name}", null, new { data.CustomerId, data.Name, data.ContractAmount }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Created project could not be loaded.");
    }

    public async Task<ProjectDetails?> UpdateAsync(
        ulong projectId,
        UpdateProjectData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE projects
            SET customer_id = @CustomerId, name = @Name,
                contract_amount = @ContractAmount, progress_percent = @ProgressPercent,
                description = @Description, planned_start_date = @PlannedStartDate,
                planned_end_date = @PlannedEndDate, version = version + 1,
                updated_at_utc = @NowUtc, updated_by_user_id = @ActorUserId
            WHERE id = @ProjectId AND version = @Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, projectId, cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        _ = await RequireActiveCustomerAsync(connection, transaction, data.CustomerId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, new { ProjectId = projectId, data.CustomerId, data.Name, data.ContractAmount, data.ProgressPercent, data.Description, data.PlannedStartDate, data.PlannedEndDate, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project.updated", "project", projectId, before.Code, $"更新项目 {data.Name}", before, new { data.CustomerId, data.Name, data.ContractAmount, data.ProgressPercent, data.PlannedStartDate, data.PlannedEndDate }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(projectId, cancellationToken);
    }

    public async Task<ProjectDetails?> TransitionAsync(
        ulong projectId,
        TransitionProjectData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE projects
            SET status = @Status,
                progress_percent = @ProgressPercent,
                actual_start_date = COALESCE(actual_start_date, @ActualStartDate),
                actual_end_date = @ActualEndDate,
                version = version + 1,
                updated_at_utc = @NowUtc, updated_by_user_id = @ActorUserId
            WHERE id = @ProjectId AND version = @Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, projectId, cancellationToken);
        if (before is null
            || !Enum.TryParse<ProjectStatus>(before.Status, true, out var current)
            || !ProjectStatusPolicy.CanTransition(current, data.Status))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var progress = data.Status == ProjectStatus.Completed ? 100m : before.ProgressPercent;
        DateTime? actualStart = data.Status == ProjectStatus.Active ? data.NowUtc.Date : null;
        DateTime? actualEnd = data.Status is ProjectStatus.Completed or ProjectStatus.Cancelled
            ? data.NowUtc.Date
            : null;
        var affected = await connection.ExecuteAsync(
            Command(sql, new { ProjectId = projectId, Status = data.Status.ToString(), ProgressPercent = progress, ActualStartDate = actualStart, ActualEndDate = actualEnd, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project.status_changed", "project", projectId, before.Code, $"项目 {before.Name} 从 {current} 转为 {data.Status}", new { status = current.ToString(), before.ProgressPercent }, new { status = data.Status.ToString(), ProgressPercent = progress }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(projectId, cancellationToken);
    }

    public async Task<ProjectDetails?> SetArchivedAsync(
        ulong projectId,
        SetProjectArchiveData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE projects
            SET archived_at_utc = CASE WHEN @Archived = 1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id = CASE WHEN @Archived = 1 THEN @ActorUserId ELSE NULL END,
                version = version + 1, updated_at_utc = @NowUtc, updated_by_user_id = @ActorUserId
            WHERE id = @ProjectId AND version = @Version
              AND ((@Archived = 1 AND archived_at_utc IS NULL)
                   OR (@Archived = 0 AND archived_at_utc IS NOT NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, projectId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, new { ProjectId = projectId, data.Archived, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(connection, transaction, data.ActorUserId, data.Archived ? "project.archived" : "project.restored", "project", projectId, before?.Code, data.Archived ? $"归档项目 {before?.Name}" : $"恢复项目 {before?.Name}", new { archived = !data.Archived }, new { archived = data.Archived }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(projectId, cancellationToken);
    }

    public async Task<ProjectMemberDetails> CreateMemberAsync(
        ulong projectId,
        CreateProjectMemberData data,
        CancellationToken cancellationToken)
    {
        const string insertSql =
            """
            INSERT INTO project_members
                (project_id, user_id, role_name, responsibility, version,
                 created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id)
            VALUES (@ProjectId, @UserId, @RoleName, @Responsibility, 1,
                    @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserReference>(
            Command("SELECT id AS Id, display_name AS DisplayName FROM users WHERE id=@UserId AND is_active=1 AND deleted_at_utc IS NULL FOR UPDATE;", new { data.UserId }, transaction, cancellationToken))
            ?? throw new NotFoundException("用户不存在或已停用。");
        var duplicate = await connection.ExecuteScalarAsync<bool>(
            Command("SELECT EXISTS(SELECT 1 FROM project_members WHERE project_id=@ProjectId AND user_id=@UserId AND deleted_at_utc IS NULL);", new { ProjectId = projectId, data.UserId }, transaction, cancellationToken));
        if (duplicate)
        {
            throw new ConflictException("该用户已经是项目成员。");
        }

        var memberId = await connection.QuerySingleAsync<ulong>(
            Command(insertSql, new { ProjectId = projectId, data.UserId, data.RoleName, data.Responsibility, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        await TouchProjectAsync(connection, transaction, projectId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project_member.created", "project_member", memberId, project.Code, $"为项目 {project.Name} 添加成员 {user.DisplayName}", null, new { projectId, data.UserId, data.RoleName }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetMemberAsync(connection, null, projectId, memberId, cancellationToken)
            ?? throw new InvalidOperationException("Created project member could not be loaded.");
    }

    public async Task<ProjectMemberDetails?> UpdateMemberAsync(
        ulong projectId,
        ulong memberId,
        UpdateProjectMemberData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE project_members
            SET role_name=@RoleName, responsibility=@Responsibility,
                version=version+1, updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId
            WHERE id=@MemberId AND project_id=@ProjectId AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var before = await GetMemberAsync(connection, transaction, projectId, memberId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command(sql, new { ProjectId = projectId, MemberId = memberId, data.RoleName, data.Responsibility, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await TouchProjectAsync(connection, transaction, projectId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project_member.updated", "project_member", memberId, project.Code, $"更新项目 {project.Name} 成员 {before?.DisplayName}", before, new { data.RoleName, data.Responsibility }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetMemberAsync(connection, null, projectId, memberId, cancellationToken);
    }

    public async Task<bool> DeleteMemberAsync(
        ulong projectId,
        ulong memberId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE project_members
            SET deleted_at_utc=@NowUtc, deleted_by_user_id=@ActorUserId,
                version=version+1, updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId
            WHERE id=@MemberId AND project_id=@ProjectId AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var before = await GetMemberAsync(connection, transaction, projectId, memberId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command(sql, new { ProjectId = projectId, MemberId = memberId, Version = version, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await TouchProjectAsync(connection, transaction, projectId, actorUserId, nowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, actorUserId, "project_member.deleted", "project_member", memberId, project.Code, $"移除项目 {project.Name} 成员 {before?.DisplayName}", before, null, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectMilestoneDetails> CreateMilestoneAsync(
        ulong projectId,
        CreateProjectMilestoneData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO project_milestones
                (project_id, name, due_date, completed_at_utc, notes, sort_order, version,
                 created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id)
            VALUES (@ProjectId, @Name, @DueDate, @CompletedAtUtc, @Notes, @SortOrder, 1,
                    @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var milestoneId = await connection.QuerySingleAsync<ulong>(
            Command(sql, new { ProjectId = projectId, data.Name, data.DueDate, CompletedAtUtc = data.IsCompleted ? data.NowUtc : (DateTime?)null, data.Notes, data.SortOrder, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        await TouchProjectAsync(connection, transaction, projectId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project_milestone.created", "project_milestone", milestoneId, project.Code, $"为项目 {project.Name} 添加里程碑 {data.Name}", null, new { projectId, data.Name, data.DueDate, data.IsCompleted }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetMilestoneAsync(connection, null, projectId, milestoneId, cancellationToken)
            ?? throw new InvalidOperationException("Created milestone could not be loaded.");
    }

    public async Task<ProjectMilestoneDetails?> UpdateMilestoneAsync(
        ulong projectId,
        ulong milestoneId,
        UpdateProjectMilestoneData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE project_milestones
            SET name=@Name, due_date=@DueDate,
                completed_at_utc=CASE
                    WHEN @IsCompleted=0 THEN NULL
                    WHEN completed_at_utc IS NULL THEN @NowUtc
                    ELSE completed_at_utc END,
                notes=@Notes, sort_order=@SortOrder, version=version+1,
                updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId
            WHERE id=@MilestoneId AND project_id=@ProjectId AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var before = await GetMilestoneAsync(connection, transaction, projectId, milestoneId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command(sql, new { ProjectId = projectId, MilestoneId = milestoneId, data.Name, data.DueDate, data.IsCompleted, data.Notes, data.SortOrder, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await TouchProjectAsync(connection, transaction, projectId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "project_milestone.updated", "project_milestone", milestoneId, project.Code, $"更新项目 {project.Name} 里程碑 {data.Name}", before, new { data.Name, data.DueDate, data.IsCompleted, data.SortOrder }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetMilestoneAsync(connection, null, projectId, milestoneId, cancellationToken);
    }

    public async Task<bool> DeleteMilestoneAsync(
        ulong projectId,
        ulong milestoneId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE project_milestones
            SET deleted_at_utc=@NowUtc, deleted_by_user_id=@ActorUserId,
                version=version+1, updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId
            WHERE id=@MilestoneId AND project_id=@ProjectId AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var project = await RequireOpenProjectAsync(connection, transaction, projectId, cancellationToken);
        var before = await GetMilestoneAsync(connection, transaction, projectId, milestoneId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command(sql, new { ProjectId = projectId, MilestoneId = milestoneId, Version = version, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await TouchProjectAsync(connection, transaction, projectId, actorUserId, nowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, actorUserId, "project_milestone.deleted", "project_milestone", milestoneId, project.Code, $"删除项目 {project.Name} 里程碑 {before?.Name}", before, null, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<ProjectDetails?> GetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT p.id AS Id, p.project_code AS Code, p.customer_id AS CustomerId,
                   c.customer_code AS CustomerCode, c.name AS CustomerName,
                   p.source_opportunity_id AS SourceOpportunityId,
                   o.opportunity_code AS SourceOpportunityCode,
                   p.name AS Name, p.status AS Status,
                   p.contract_amount AS ContractAmount,
                   p.progress_percent AS ProgressPercent,
                   p.description AS Description, p.planned_start_date AS PlannedStartDate,
                   p.planned_end_date AS PlannedEndDate, p.actual_start_date AS ActualStartDate,
                   p.actual_end_date AS ActualEndDate, p.archived_at_utc AS ArchivedAtUtc,
                   p.version AS Version, p.created_at_utc AS CreatedAtUtc,
                   p.updated_at_utc AS UpdatedAtUtc
            FROM projects p
            INNER JOIN customers c ON c.id=p.customer_id
            LEFT JOIN opportunities o ON o.id=p.source_opportunity_id
            WHERE p.id=@ProjectId;

            SELECT pm.id AS Id, pm.project_id AS ProjectId, pm.user_id AS UserId,
                   u.username AS Username, u.display_name AS DisplayName,
                   pm.role_name AS RoleName, pm.responsibility AS Responsibility,
                   pm.version AS Version, pm.created_at_utc AS CreatedAtUtc,
                   pm.updated_at_utc AS UpdatedAtUtc
            FROM project_members pm
            INNER JOIN users u ON u.id=pm.user_id
            WHERE pm.project_id=@ProjectId AND pm.deleted_at_utc IS NULL
            ORDER BY pm.created_at_utc, pm.id;

            SELECT id AS Id, project_id AS ProjectId, name AS Name,
                   due_date AS DueDate, completed_at_utc AS CompletedAtUtc,
                   notes AS Notes, sort_order AS SortOrder, version AS Version,
                   created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
            FROM project_milestones
            WHERE project_id=@ProjectId AND deleted_at_utc IS NULL
            ORDER BY sort_order, due_date, id;
            """;
        using var result = await connection.QueryMultipleAsync(Command(sql, new { ProjectId = projectId }, transaction, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<ProjectHeader>();
        if (header is null)
        {
            return null;
        }

        var members = (await result.ReadAsync<ProjectMemberDetails>()).AsList();
        var milestones = (await result.ReadAsync<ProjectMilestoneDetails>()).AsList();
        return header.ToDetails(members, milestones);
    }

    private static Task<ProjectSnapshot?> GetSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong projectId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProjectSnapshot>(
            Command(
                "SELECT id AS Id, project_code AS Code, name AS Name, status AS Status, progress_percent AS ProgressPercent, archived_at_utc AS ArchivedAtUtc, version AS Version FROM projects WHERE id=@ProjectId FOR UPDATE;",
                new { ProjectId = projectId },
                transaction,
                cancellationToken));

    private static async Task<ProjectSnapshot> RequireOpenProjectAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong projectId,
        CancellationToken cancellationToken)
    {
        var project = await GetSnapshotAsync(connection, transaction, projectId, cancellationToken)
            ?? throw new NotFoundException("项目不存在。");
        if (project.ArchivedAtUtc.HasValue
            || !Enum.TryParse<ProjectStatus>(project.Status, true, out var status)
            || ProjectStatusPolicy.IsTerminal(status))
        {
            throw new ConflictException("项目当前状态不能修改成员或里程碑。");
        }

        return project;
    }

    private static async Task<CustomerReference> RequireActiveCustomerAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<CustomerReference>(
            Command("SELECT id AS Id, name AS Name FROM customers WHERE id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;", new { CustomerId = customerId }, transaction, cancellationToken))
        ?? throw new NotFoundException("客户不存在或已经归档。");

    private static Task<ProjectMemberDetails?> GetMemberAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong projectId,
        ulong memberId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProjectMemberDetails>(
            Command(
                "SELECT pm.id AS Id, pm.project_id AS ProjectId, pm.user_id AS UserId, u.username AS Username, u.display_name AS DisplayName, pm.role_name AS RoleName, pm.responsibility AS Responsibility, pm.version AS Version, pm.created_at_utc AS CreatedAtUtc, pm.updated_at_utc AS UpdatedAtUtc FROM project_members pm INNER JOIN users u ON u.id=pm.user_id WHERE pm.id=@MemberId AND pm.project_id=@ProjectId AND pm.deleted_at_utc IS NULL;",
                new { ProjectId = projectId, MemberId = memberId },
                transaction,
                cancellationToken));

    private static Task<ProjectMilestoneDetails?> GetMilestoneAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong projectId,
        ulong milestoneId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ProjectMilestoneDetails>(
            Command(
                "SELECT id AS Id, project_id AS ProjectId, name AS Name, due_date AS DueDate, completed_at_utc AS CompletedAtUtc, notes AS Notes, sort_order AS SortOrder, version AS Version, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc FROM project_milestones WHERE id=@MilestoneId AND project_id=@ProjectId AND deleted_at_utc IS NULL;",
                new { ProjectId = projectId, MilestoneId = milestoneId },
                transaction,
                cancellationToken));

    private static Task<int> TouchProjectAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong projectId,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Command("UPDATE projects SET updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId WHERE id=@ProjectId;", new { ProjectId = projectId, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));

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
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            Command(
                "INSERT INTO audit_logs (occurred_at_utc,actor_user_id,action,entity_type,entity_id,entity_code,summary,before_json,after_json) VALUES (@NowUtc,@ActorUserId,@Action,@EntityType,@EntityId,@EntityCode,@Summary,@BeforeJson,@AfterJson);",
                new { NowUtc = nowUtc, ActorUserId = actorUserId, Action = action, EntityType = entityType, EntityId = entityId, EntityCode = entityCode, Summary = summary, BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = after is null ? null : JsonSerializer.Serialize(after) },
                transaction,
                cancellationToken));

    private static CommandDefinition Command(string sql, object? parameters, DbTransaction? transaction, CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private sealed class ProjectSummaryRow
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string CustomerName { get; init; }
        public required string Name { get; init; }
        public required string Status { get; init; }
        public decimal ContractAmount { get; init; }
        public decimal ProgressPercent { get; init; }
        public DateTime? PlannedEndDate { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public string? SourceOpportunityCode { get; init; }
        public int MemberCount { get; init; }
        public int MilestoneCount { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ProjectSummary ToDetails() =>
            new(Id, Code, CustomerId, CustomerCode, CustomerName, Name, Enum.Parse<ProjectStatus>(Status, true), ContractAmount, ProgressPercent, PlannedEndDate, ArchivedAtUtc.HasValue, SourceOpportunityCode, MemberCount, MilestoneCount, Version, UpdatedAtUtc);
    }

    private sealed class ProjectHeader
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string CustomerName { get; init; }
        public ulong? SourceOpportunityId { get; init; }
        public string? SourceOpportunityCode { get; init; }
        public required string Name { get; init; }
        public required string Status { get; init; }
        public decimal ContractAmount { get; init; }
        public decimal ProgressPercent { get; init; }
        public string? Description { get; init; }
        public DateTime? PlannedStartDate { get; init; }
        public DateTime? PlannedEndDate { get; init; }
        public DateTime? ActualStartDate { get; init; }
        public DateTime? ActualEndDate { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ProjectDetails ToDetails(IReadOnlyList<ProjectMemberDetails> members, IReadOnlyList<ProjectMilestoneDetails> milestones) =>
            new(Id, Code, CustomerId, CustomerCode, CustomerName, SourceOpportunityId, SourceOpportunityCode, Name, Enum.Parse<ProjectStatus>(Status, true), ContractAmount, ProgressPercent, Description, PlannedStartDate, PlannedEndDate, ActualStartDate, ActualEndDate, ArchivedAtUtc.HasValue, ArchivedAtUtc, Version, CreatedAtUtc, UpdatedAtUtc, members, milestones);
    }

    private sealed class ProjectSnapshot
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string Status { get; init; }
        public decimal ProgressPercent { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class CustomerReference
    {
        public ulong Id { get; init; }
        public required string Name { get; init; }
    }

    private sealed class UserReference
    {
        public ulong Id { get; init; }
        public required string DisplayName { get; init; }
    }
}
