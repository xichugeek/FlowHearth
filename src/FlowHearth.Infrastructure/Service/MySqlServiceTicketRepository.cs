using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Service;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Service;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Service;

public sealed class MySqlServiceTicketRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null)
    : IServiceTicketRepository
{
    public async Task<PagedResult<ServiceTicketSummary>> ListAsync(ServiceTicketListCriteria criteria, CancellationToken cancellationToken)
    {
        var archiveClause = criteria.ArchiveMode switch
        {
            ServiceTicketArchiveMode.Active => "s.archived_at_utc IS NULL",
            ServiceTicketArchiveMode.Archived => "s.archived_at_utc IS NOT NULL",
            ServiceTicketArchiveMode.All => "1=1",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var orderBy = criteria.SortBy.ToLowerInvariant() switch
        {
            "code" => "s.service_code",
            "title" => "s.title",
            "priority" => "s.priority",
            "status" => "s.status",
            "customer" => "c.name",
            "reportedat" => "s.reported_at_utc",
            "updatedat" => "s.updated_at_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var direction = criteria.SortDescending ? "DESC" : "ASC";
        var sql = $"""
            SELECT COUNT(*)
            FROM service_tickets s
            INNER JOIN customers c ON c.id=s.customer_id
            LEFT JOIN projects p ON p.id=s.project_id
            LEFT JOIN equipment e ON e.id=s.equipment_id
            WHERE {archiveClause}
              AND (@Priority IS NULL OR s.priority=@Priority)
              AND (@Status IS NULL OR s.status=@Status)
              AND (@CustomerId IS NULL OR s.customer_id=@CustomerId)
              AND (@ProjectId IS NULL OR s.project_id=@ProjectId)
              AND (@EquipmentId IS NULL OR s.equipment_id=@EquipmentId)
              AND (@AssignedUserId IS NULL OR s.assigned_to_user_id=@AssignedUserId)
              AND (@SearchPattern IS NULL OR CONVERT(s.service_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=' OR s.title LIKE @SearchPattern ESCAPE '=' OR s.description LIKE @SearchPattern ESCAPE '=' OR c.name LIKE @SearchPattern ESCAPE '=' OR p.name LIKE @SearchPattern ESCAPE '=' OR e.name LIKE @SearchPattern ESCAPE '=' OR e.serial_number LIKE @SearchPattern ESCAPE '=');

            SELECT s.id AS Id, s.service_code AS Code, s.customer_id AS CustomerId,
                   c.customer_code AS CustomerCode, c.name AS CustomerName,
                   s.project_id AS ProjectId, p.project_code AS ProjectCode, p.name AS ProjectName,
                   s.equipment_id AS EquipmentId, e.equipment_code AS EquipmentCode, e.name AS EquipmentName,
                   s.title AS Title, s.priority AS Priority, s.status AS Status,
                   s.assigned_to_user_id AS AssignedUserId, u.display_name AS AssignedDisplayName,
                   s.reported_at_utc AS ReportedAtUtc, s.downtime_minutes AS DowntimeMinutes,
                   s.archived_at_utc AS ArchivedAtUtc,
                   (SELECT COUNT(*) FROM service_records r WHERE r.service_ticket_id=s.id AND r.deleted_at_utc IS NULL) AS RecordCount,
                   s.version AS Version, s.updated_at_utc AS UpdatedAtUtc
            FROM service_tickets s
            INNER JOIN customers c ON c.id=s.customer_id
            LEFT JOIN projects p ON p.id=s.project_id
            LEFT JOIN equipment e ON e.id=s.equipment_id
            LEFT JOIN users u ON u.id=s.assigned_to_user_id
            WHERE {archiveClause}
              AND (@Priority IS NULL OR s.priority=@Priority)
              AND (@Status IS NULL OR s.status=@Status)
              AND (@CustomerId IS NULL OR s.customer_id=@CustomerId)
              AND (@ProjectId IS NULL OR s.project_id=@ProjectId)
              AND (@EquipmentId IS NULL OR s.equipment_id=@EquipmentId)
              AND (@AssignedUserId IS NULL OR s.assigned_to_user_id=@AssignedUserId)
              AND (@SearchPattern IS NULL OR CONVERT(s.service_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=' OR s.title LIKE @SearchPattern ESCAPE '=' OR s.description LIKE @SearchPattern ESCAPE '=' OR c.name LIKE @SearchPattern ESCAPE '=' OR p.name LIKE @SearchPattern ESCAPE '=' OR e.name LIKE @SearchPattern ESCAPE '=' OR e.serial_number LIKE @SearchPattern ESCAPE '=')
            ORDER BY {orderBy} {direction}, s.id {direction}
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new { Priority = criteria.Priority?.ToString(), Status = criteria.Status?.ToString(), criteria.CustomerId, criteria.ProjectId, criteria.EquipmentId, criteria.AssignedUserId, SearchPattern = MySqlLikePattern.Contains(criteria.Search), criteria.PageSize, Offset = ((long)criteria.Page - 1) * criteria.PageSize };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(Command(sql, parameters, null, cancellationToken));
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<SummaryRow>()).Select(row => row.ToDetails()).ToArray();
        return new PagedResult<ServiceTicketSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<ServiceTicketDetails?> GetAsync(ulong ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, ticketId, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<ServiceAssignee>(Command("SELECT id AS UserId, username AS Username, display_name AS DisplayName, email AS Email FROM users WHERE is_active=1 AND deleted_at_utc IS NULL ORDER BY display_name,id;", null, null, cancellationToken))).AsList();
    }

    public async Task<ServiceTicketDetails> CreateAsync(ServiceTicketWriteData data, ulong? assignedUserId, CancellationToken cancellationToken)
    {
        const string sequenceSql = "INSERT INTO number_sequences (sequence_name,current_value,updated_at_utc) VALUES (@SequenceName,LAST_INSERT_ID(1),@NowUtc) ON DUPLICATE KEY UPDATE current_value=LAST_INSERT_ID(current_value+1),updated_at_utc=VALUES(updated_at_utc); SELECT LAST_INSERT_ID();";
        const string insertSql = """
            INSERT INTO service_tickets
                (service_code,customer_id,project_id,equipment_id,title,description,priority,status,assigned_to_user_id,reported_at_utc,responded_at_utc,root_cause,solution,downtime_minutes,version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES (@Code,@CustomerId,@ProjectId,@EquipmentId,@Title,@Description,@Priority,@Status,@AssignedUserId,@ReportedAtUtc,@RespondedAtUtc,@RootCause,@Solution,@DowntimeMinutes,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var links = await RequireLinksAsync(connection, transaction, data.CustomerId, data.ProjectId, data.EquipmentId, cancellationToken);
        var assignee = await GetAssigneeAsync(connection, transaction, assignedUserId, cancellationToken);
        var sequence = await connection.QuerySingleAsync<ulong>(Command(sequenceSql, new { SequenceName = $"service:{data.NowUtc.Year:D4}", data.NowUtc }, transaction, cancellationToken));
        var prefix = runtimeSettingsProvider is null
            ? ServiceTicketCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Service;
        var code = ServiceTicketCode.Format(prefix, data.NowUtc.Year, sequence);
        var id = await connection.QuerySingleAsync<ulong>(Command(insertSql, new { Code = code, data.CustomerId, data.ProjectId, data.EquipmentId, data.Title, data.Description, Priority = data.Priority.ToString(), Status = ServiceTicketStatus.New.ToString(), AssignedUserId = assignedUserId, data.ReportedAtUtc, RespondedAtUtc = assignedUserId.HasValue ? data.NowUtc : (DateTime?)null, data.RootCause, data.Solution, data.DowntimeMinutes, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_ticket.created", "service_ticket", id, code, $"为客户 {links.CustomerName} 创建服务工单 {data.Title}", null, new { data.CustomerId, data.ProjectId, data.EquipmentId, data.Title, data.Priority }, data.NowUtc, cancellationToken);
        if (assignedUserId.HasValue) await InsertSystemRecordAsync(connection, transaction, id, ServiceRecordType.Assignment, $"工单创建时指派给 {assignee!.DisplayName}。", null, null, data.ActorUserId, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Created service ticket could not be loaded.");
    }

    public async Task<ServiceTicketDetails?> UpdateAsync(ulong ticketId, ServiceTicketWriteData data, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE service_tickets SET customer_id=@CustomerId,project_id=@ProjectId,equipment_id=@EquipmentId,title=@Title,description=@Description,priority=@Priority,reported_at_utc=@ReportedAtUtc,root_cause=@RootCause,solution=@Solution,downtime_minutes=@DowntimeMinutes,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@TicketId AND version=@Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, ticketId, cancellationToken);
        if (before is null || before.ArchivedAtUtc.HasValue) { await transaction.RollbackAsync(cancellationToken); return null; }
        _ = await RequireLinksAsync(connection, transaction, data.CustomerId, data.ProjectId, data.EquipmentId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command(sql, new { TicketId = ticketId, data.CustomerId, data.ProjectId, data.EquipmentId, data.Title, data.Description, Priority = data.Priority.ToString(), data.ReportedAtUtc, data.RootCause, data.Solution, data.DowntimeMinutes, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_ticket.updated", "service_ticket", ticketId, before.Code, $"更新服务工单 {data.Title}", before, new { data.Title, data.Priority, data.RootCause, data.Solution, data.DowntimeMinutes }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ticketId, cancellationToken);
    }

    public async Task<ServiceTicketDetails?> AssignAsync(ulong ticketId, AssignServiceTicketData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, ticketId, cancellationToken);
        if (before is null || before.ArchivedAtUtc.HasValue) { await transaction.RollbackAsync(cancellationToken); return null; }
        var assignee = await GetAssigneeAsync(connection, transaction, data.UserId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command("UPDATE service_tickets SET assigned_to_user_id=@UserId,responded_at_utc=CASE WHEN @UserId IS NOT NULL AND responded_at_utc IS NULL THEN @NowUtc ELSE responded_at_utc END,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@TicketId AND version=@Version AND archived_at_utc IS NULL;", new { TicketId = ticketId, data.UserId, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
        var content = data.UserId.HasValue ? $"工单指派给 {assignee!.DisplayName}。" : "取消工单指派。";
        await InsertSystemRecordAsync(connection, transaction, ticketId, ServiceRecordType.Assignment, content, null, null, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_ticket.assigned", "service_ticket", ticketId, before.Code, content, new { before.AssignedUserId }, new { AssignedUserId = data.UserId }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ticketId, cancellationToken);
    }

    public async Task<ServiceTicketDetails?> TransitionAsync(ulong ticketId, TransitionServiceTicketData data, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE service_tickets SET status=@Status,
                responded_at_utc=CASE WHEN @Status='InProgress' AND responded_at_utc IS NULL THEN @NowUtc ELSE responded_at_utc END,
                resolved_at_utc=CASE WHEN @Status='Resolved' THEN @NowUtc WHEN @Status='InProgress' THEN NULL ELSE resolved_at_utc END,
                closed_at_utc=CASE WHEN @Status='Closed' THEN @NowUtc WHEN @Status='InProgress' THEN NULL ELSE closed_at_utc END,
                root_cause=@RootCause,solution=@Solution,downtime_minutes=@DowntimeMinutes,
                version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@TicketId AND version=@Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, ticketId, cancellationToken);
        if (before is null || before.ArchivedAtUtc.HasValue) { await transaction.RollbackAsync(cancellationToken); return null; }
        var affected = await connection.ExecuteAsync(Command(sql, new { TicketId = ticketId, Status = data.Status.ToString(), data.RootCause, data.Solution, data.DowntimeMinutes, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
        var content = $"工单状态从 {before.Status} 变更为 {data.Status}。";
        await InsertSystemRecordAsync(connection, transaction, ticketId, ServiceRecordType.StatusChange, content, Enum.Parse<ServiceTicketStatus>(before.Status, true), data.Status, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_ticket.status_changed", "service_ticket", ticketId, before.Code, content, new { before.Status }, new { data.Status, data.RootCause, data.Solution, data.DowntimeMinutes }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ticketId, cancellationToken);
    }

    public async Task<ServiceTicketDetails?> SetArchivedAsync(ulong ticketId, SetServiceTicketArchiveData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, ticketId, cancellationToken);
        if (before is null) { await transaction.RollbackAsync(cancellationToken); return null; }
        var affected = await connection.ExecuteAsync(Command("UPDATE service_tickets SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@TicketId AND version=@Version AND ((@Archived=1 AND archived_at_utc IS NULL) OR (@Archived=0 AND archived_at_utc IS NOT NULL));", new { TicketId = ticketId, data.Archived, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
        await WriteAuditAsync(connection, transaction, data.ActorUserId, data.Archived ? "service_ticket.archived" : "service_ticket.restored", "service_ticket", ticketId, before.Code, data.Archived ? $"归档服务工单 {before.Title}" : $"恢复服务工单 {before.Title}", new { Archived = before.ArchivedAtUtc.HasValue }, new { data.Archived }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(ticketId, cancellationToken);
    }

    public async Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, ServiceRecordWriteData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var ticket = await RequireOpenTicketAsync(connection, transaction, ticketId, cancellationToken);
        var id = await connection.QuerySingleAsync<ulong>(Command("INSERT INTO service_records (service_ticket_id,record_type,content,duration_minutes,occurred_at_utc,version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id) VALUES (@TicketId,@RecordType,@Content,@DurationMinutes,@OccurredAtUtc,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId); SELECT LAST_INSERT_ID();", new { TicketId = ticketId, RecordType = data.RecordType.ToString(), data.Content, data.DurationMinutes, data.OccurredAtUtc, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        await TouchTicketAsync(connection, transaction, ticketId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_record.created", "service_record", id, ticket.Code, $"为工单 {ticket.Title} 添加{data.RecordType}记录", null, new { ticketId, data.RecordType, data.Content, data.DurationMinutes }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRecordAsync(connection, null, ticketId, id, cancellationToken) ?? throw new InvalidOperationException("Created service record could not be loaded.");
    }

    public async Task<ServiceRecordDetails?> UpdateRecordAsync(ulong ticketId, ulong recordId, ServiceRecordWriteData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var ticket = await RequireOpenTicketAsync(connection, transaction, ticketId, cancellationToken);
        var before = await GetRecordAsync(connection, transaction, ticketId, recordId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command("UPDATE service_records SET record_type=@RecordType,content=@Content,duration_minutes=@DurationMinutes,occurred_at_utc=@OccurredAtUtc,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@RecordId AND service_ticket_id=@TicketId AND version=@Version AND deleted_at_utc IS NULL AND record_type IN ('Note','Diagnosis','Action');", new { RecordId = recordId, TicketId = ticketId, RecordType = data.RecordType.ToString(), data.Content, data.DurationMinutes, data.OccurredAtUtc, data.Version, data.NowUtc, data.ActorUserId }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return null; }
        await TouchTicketAsync(connection, transaction, ticketId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "service_record.updated", "service_record", recordId, ticket.Code, $"更新工单 {ticket.Title} 服务记录", before, new { data.RecordType, data.Content, data.DurationMinutes }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRecordAsync(connection, null, ticketId, recordId, cancellationToken);
    }

    public async Task<bool> DeleteRecordAsync(ulong ticketId, ulong recordId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var ticket = await RequireOpenTicketAsync(connection, transaction, ticketId, cancellationToken);
        var before = await GetRecordAsync(connection, transaction, ticketId, recordId, cancellationToken);
        var affected = await connection.ExecuteAsync(Command("UPDATE service_records SET deleted_at_utc=@NowUtc,deleted_by_user_id=@ActorUserId,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@RecordId AND service_ticket_id=@TicketId AND version=@Version AND deleted_at_utc IS NULL AND record_type IN ('Note','Diagnosis','Action');", new { RecordId = recordId, TicketId = ticketId, Version = version, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
        if (affected != 1) { await transaction.RollbackAsync(cancellationToken); return false; }
        await TouchTicketAsync(connection, transaction, ticketId, actorUserId, nowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, actorUserId, "service_record.deleted", "service_record", recordId, ticket.Code, $"删除工单 {ticket.Title} 服务记录", before, null, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<ServiceTicketDetails?> GetAsync(DbConnection connection, DbTransaction? transaction, ulong ticketId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.id AS Id,s.service_code AS Code,s.customer_id AS CustomerId,c.customer_code AS CustomerCode,c.name AS CustomerName,s.project_id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,s.equipment_id AS EquipmentId,e.equipment_code AS EquipmentCode,e.name AS EquipmentName,s.title AS Title,s.description AS Description,s.priority AS Priority,s.status AS Status,s.assigned_to_user_id AS AssignedUserId,u.display_name AS AssignedDisplayName,s.reported_at_utc AS ReportedAtUtc,s.responded_at_utc AS RespondedAtUtc,s.resolved_at_utc AS ResolvedAtUtc,s.closed_at_utc AS ClosedAtUtc,s.root_cause AS RootCause,s.solution AS Solution,s.downtime_minutes AS DowntimeMinutes,s.archived_at_utc AS ArchivedAtUtc,s.version AS Version,s.created_at_utc AS CreatedAtUtc,s.updated_at_utc AS UpdatedAtUtc
            FROM service_tickets s INNER JOIN customers c ON c.id=s.customer_id LEFT JOIN projects p ON p.id=s.project_id LEFT JOIN equipment e ON e.id=s.equipment_id LEFT JOIN users u ON u.id=s.assigned_to_user_id WHERE s.id=@TicketId;
            SELECT r.id AS Id,r.service_ticket_id AS ServiceTicketId,r.record_type AS RecordType,r.content AS Content,r.from_status AS FromStatus,r.to_status AS ToStatus,r.duration_minutes AS DurationMinutes,r.occurred_at_utc AS OccurredAtUtc,r.created_by_user_id AS CreatedByUserId,u.display_name AS CreatedByDisplayName,r.version AS Version,r.created_at_utc AS CreatedAtUtc,r.updated_at_utc AS UpdatedAtUtc
            FROM service_records r LEFT JOIN users u ON u.id=r.created_by_user_id WHERE r.service_ticket_id=@TicketId AND r.deleted_at_utc IS NULL ORDER BY r.occurred_at_utc,r.id;
            """;
        using var result = await connection.QueryMultipleAsync(Command(sql, new { TicketId = ticketId }, transaction, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<HeaderRow>();
        if (header is null) return null;
        var records = (await result.ReadAsync<RecordRow>()).Select(row => row.ToDetails()).ToArray();
        return header.ToDetails(records);
    }

    private static Task<TicketSnapshot?> GetSnapshotAsync(DbConnection connection, DbTransaction transaction, ulong ticketId, CancellationToken cancellationToken) => connection.QuerySingleOrDefaultAsync<TicketSnapshot>(Command("SELECT id AS Id,service_code AS Code,title AS Title,status AS Status,assigned_to_user_id AS AssignedUserId,archived_at_utc AS ArchivedAtUtc,version AS Version FROM service_tickets WHERE id=@TicketId FOR UPDATE;", new { TicketId = ticketId }, transaction, cancellationToken));
    private static async Task<TicketSnapshot> RequireOpenTicketAsync(DbConnection connection, DbTransaction transaction, ulong ticketId, CancellationToken cancellationToken) { var ticket = await GetSnapshotAsync(connection, transaction, ticketId, cancellationToken) ?? throw new NotFoundException("服务工单不存在。"); if (ticket.ArchivedAtUtc.HasValue || !Enum.TryParse<ServiceTicketStatus>(ticket.Status, true, out var status) || ServiceTicketStatusPolicy.IsTerminal(status)) throw new ConflictException("工单当前状态不能修改服务记录。"); return ticket; }

    private static async Task<LinkReferences> RequireLinksAsync(DbConnection connection, DbTransaction transaction, ulong customerId, ulong? projectId, ulong? equipmentId, CancellationToken cancellationToken)
    {
        var customer = await connection.QuerySingleOrDefaultAsync<NamedReference>(Command("SELECT id AS Id,name AS Name FROM customers WHERE id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;", new { CustomerId = customerId }, transaction, cancellationToken)) ?? throw new NotFoundException("客户不存在或已经归档。");
        NamedReference? project = null;
        if (projectId.HasValue) project = await connection.QuerySingleOrDefaultAsync<NamedReference>(Command("SELECT id AS Id,name AS Name FROM projects WHERE id=@ProjectId AND customer_id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;", new { ProjectId = projectId.Value, CustomerId = customerId }, transaction, cancellationToken)) ?? throw new NotFoundException("项目不存在、已经归档或不属于所选客户。");
        EquipmentReference? equipment = null;
        if (equipmentId.HasValue) equipment = await connection.QuerySingleOrDefaultAsync<EquipmentReference>(Command("SELECT id AS Id,name AS Name,project_id AS ProjectId FROM equipment WHERE id=@EquipmentId AND customer_id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;", new { EquipmentId = equipmentId.Value, CustomerId = customerId }, transaction, cancellationToken)) ?? throw new NotFoundException("设备不存在、已经归档或不属于所选客户。");
        if (projectId.HasValue && equipment?.ProjectId != projectId) throw new FlowHearthValidationException(new Dictionary<string, string[]> { ["equipmentId"] = ["设备不属于所选项目。"] });
        return new LinkReferences(customer.Name, project?.Name, equipment?.Name);
    }

    private static async Task<NamedReference?> GetAssigneeAsync(DbConnection connection, DbTransaction transaction, ulong? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return null;
        return await connection.QuerySingleOrDefaultAsync<NamedReference>(Command("SELECT id AS Id,display_name AS Name FROM users WHERE id=@UserId AND is_active=1 AND deleted_at_utc IS NULL FOR UPDATE;", new { UserId = userId.Value }, transaction, cancellationToken)) ?? throw new NotFoundException("指派用户不存在或不可用。");
    }

    private static Task<ulong> InsertSystemRecordAsync(DbConnection connection, DbTransaction transaction, ulong ticketId, ServiceRecordType type, string content, ServiceTicketStatus? fromStatus, ServiceTicketStatus? toStatus, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => connection.QuerySingleAsync<ulong>(Command("INSERT INTO service_records (service_ticket_id,record_type,content,from_status,to_status,occurred_at_utc,version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id) VALUES (@TicketId,@RecordType,@Content,@FromStatus,@ToStatus,@NowUtc,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId); SELECT LAST_INSERT_ID();", new { TicketId = ticketId, RecordType = type.ToString(), Content = content, FromStatus = fromStatus?.ToString(), ToStatus = toStatus?.ToString(), NowUtc = nowUtc, ActorUserId = actorUserId }, transaction, cancellationToken));

    private static Task<ServiceRecordDetails?> GetRecordAsync(DbConnection connection, DbTransaction? transaction, ulong ticketId, ulong recordId, CancellationToken cancellationToken) => GetRecordRowAsync(connection, transaction, ticketId, recordId, cancellationToken);
    private static async Task<ServiceRecordDetails?> GetRecordRowAsync(DbConnection connection, DbTransaction? transaction, ulong ticketId, ulong recordId, CancellationToken cancellationToken) { var row = await connection.QuerySingleOrDefaultAsync<RecordRow>(Command("SELECT r.id AS Id,r.service_ticket_id AS ServiceTicketId,r.record_type AS RecordType,r.content AS Content,r.from_status AS FromStatus,r.to_status AS ToStatus,r.duration_minutes AS DurationMinutes,r.occurred_at_utc AS OccurredAtUtc,r.created_by_user_id AS CreatedByUserId,u.display_name AS CreatedByDisplayName,r.version AS Version,r.created_at_utc AS CreatedAtUtc,r.updated_at_utc AS UpdatedAtUtc FROM service_records r LEFT JOIN users u ON u.id=r.created_by_user_id WHERE r.id=@RecordId AND r.service_ticket_id=@TicketId AND r.deleted_at_utc IS NULL;", new { RecordId = recordId, TicketId = ticketId }, transaction, cancellationToken)); return row?.ToDetails(); }
    private static Task<int> TouchTicketAsync(DbConnection connection, DbTransaction transaction, ulong ticketId, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => connection.ExecuteAsync(Command("UPDATE service_tickets SET updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@TicketId;", new { TicketId = ticketId, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
    private static Task<int> WriteAuditAsync(DbConnection connection, DbTransaction transaction, ulong actorUserId, string action, string entityType, ulong entityId, string? entityCode, string summary, object? before, object? after, DateTime nowUtc, CancellationToken cancellationToken) => connection.ExecuteAsync(Command("INSERT INTO audit_logs (occurred_at_utc,actor_user_id,action,entity_type,entity_id,entity_code,summary,before_json,after_json) VALUES (@NowUtc,@ActorUserId,@Action,@EntityType,@EntityId,@EntityCode,@Summary,@BeforeJson,@AfterJson);", new { NowUtc = nowUtc, ActorUserId = actorUserId, Action = action, EntityType = entityType, EntityId = entityId, EntityCode = entityCode, Summary = summary, BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = after is null ? null : JsonSerializer.Serialize(after) }, transaction, cancellationToken));
    private static CommandDefinition Command(string sql, object? parameters, DbTransaction? transaction, CancellationToken cancellationToken) => new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private sealed class SummaryRow { public ulong Id { get; init; } public required string Code { get; init; } public ulong CustomerId { get; init; } public required string CustomerCode { get; init; } public required string CustomerName { get; init; } public ulong? ProjectId { get; init; } public string? ProjectCode { get; init; } public string? ProjectName { get; init; } public ulong? EquipmentId { get; init; } public string? EquipmentCode { get; init; } public string? EquipmentName { get; init; } public required string Title { get; init; } public required string Priority { get; init; } public required string Status { get; init; } public ulong? AssignedUserId { get; init; } public string? AssignedDisplayName { get; init; } public DateTime ReportedAtUtc { get; init; } public uint DowntimeMinutes { get; init; } public DateTime? ArchivedAtUtc { get; init; } public int RecordCount { get; init; } public ulong Version { get; init; } public DateTime UpdatedAtUtc { get; init; } public ServiceTicketSummary ToDetails() => new(Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode, ProjectName, EquipmentId, EquipmentCode, EquipmentName, Title, Enum.Parse<ServiceTicketPriority>(Priority, true), Enum.Parse<ServiceTicketStatus>(Status, true), AssignedUserId, AssignedDisplayName, ReportedAtUtc, DowntimeMinutes, ArchivedAtUtc.HasValue, RecordCount, Version, UpdatedAtUtc); }
    private sealed class HeaderRow { public ulong Id { get; init; } public required string Code { get; init; } public ulong CustomerId { get; init; } public required string CustomerCode { get; init; } public required string CustomerName { get; init; } public ulong? ProjectId { get; init; } public string? ProjectCode { get; init; } public string? ProjectName { get; init; } public ulong? EquipmentId { get; init; } public string? EquipmentCode { get; init; } public string? EquipmentName { get; init; } public required string Title { get; init; } public string? Description { get; init; } public required string Priority { get; init; } public required string Status { get; init; } public ulong? AssignedUserId { get; init; } public string? AssignedDisplayName { get; init; } public DateTime ReportedAtUtc { get; init; } public DateTime? RespondedAtUtc { get; init; } public DateTime? ResolvedAtUtc { get; init; } public DateTime? ClosedAtUtc { get; init; } public string? RootCause { get; init; } public string? Solution { get; init; } public uint DowntimeMinutes { get; init; } public DateTime? ArchivedAtUtc { get; init; } public ulong Version { get; init; } public DateTime CreatedAtUtc { get; init; } public DateTime UpdatedAtUtc { get; init; } public ServiceTicketDetails ToDetails(IReadOnlyList<ServiceRecordDetails> records) => new(Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode, ProjectName, EquipmentId, EquipmentCode, EquipmentName, Title, Description, Enum.Parse<ServiceTicketPriority>(Priority, true), Enum.Parse<ServiceTicketStatus>(Status, true), AssignedUserId, AssignedDisplayName, ReportedAtUtc, RespondedAtUtc, ResolvedAtUtc, ClosedAtUtc, RootCause, Solution, DowntimeMinutes, ArchivedAtUtc.HasValue, ArchivedAtUtc, Version, CreatedAtUtc, UpdatedAtUtc, records); }
    private sealed class RecordRow { public ulong Id { get; init; } public ulong ServiceTicketId { get; init; } public required string RecordType { get; init; } public required string Content { get; init; } public string? FromStatus { get; init; } public string? ToStatus { get; init; } public uint? DurationMinutes { get; init; } public DateTime OccurredAtUtc { get; init; } public ulong? CreatedByUserId { get; init; } public string? CreatedByDisplayName { get; init; } public ulong Version { get; init; } public DateTime CreatedAtUtc { get; init; } public DateTime UpdatedAtUtc { get; init; } public ServiceRecordDetails ToDetails() => new(Id, ServiceTicketId, Enum.Parse<ServiceRecordType>(RecordType, true), Content, string.IsNullOrEmpty(FromStatus) ? null : Enum.Parse<ServiceTicketStatus>(FromStatus, true), string.IsNullOrEmpty(ToStatus) ? null : Enum.Parse<ServiceTicketStatus>(ToStatus, true), DurationMinutes, OccurredAtUtc, CreatedByUserId, CreatedByDisplayName, Version, CreatedAtUtc, UpdatedAtUtc); }
    private sealed class TicketSnapshot { public ulong Id { get; init; } public required string Code { get; init; } public required string Title { get; init; } public required string Status { get; init; } public ulong? AssignedUserId { get; init; } public DateTime? ArchivedAtUtc { get; init; } public ulong Version { get; init; } }
    private sealed class NamedReference { public ulong Id { get; init; } public required string Name { get; init; } public string DisplayName => Name; }
    private sealed class EquipmentReference { public ulong Id { get; init; } public required string Name { get; init; } public ulong? ProjectId { get; init; } }
    private sealed record LinkReferences(string CustomerName, string? ProjectName, string? EquipmentName);
}
