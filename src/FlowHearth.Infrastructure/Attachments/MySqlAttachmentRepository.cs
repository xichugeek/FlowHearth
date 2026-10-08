using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Common;
using FlowHearth.Domain.Attachments;

namespace FlowHearth.Infrastructure.Attachments;

public sealed class MySqlAttachmentRepository(IDbConnectionFactory connectionFactory)
    : IAttachmentRepository
{
    private const string AttachmentSelect =
        """
        SELECT a.id AS Id,a.entity_type AS EntityType,a.entity_id AS EntityId,
               a.entity_code AS EntityCode,a.original_file_name AS OriginalFileName,
               a.storage_key AS StorageKey,a.content_type AS ContentType,
               a.size_bytes AS SizeBytes,a.sha256 AS Sha256,
               a.description AS Description,a.version AS Version,
               a.uploaded_at_utc AS UploadedAtUtc,
               a.uploaded_by_user_id AS UploadedByUserId,
               u.display_name AS UploadedByDisplayName
        FROM attachments AS a
        LEFT JOIN users AS u ON u.id=a.uploaded_by_user_id
        """;

    public async Task<AttachmentEntityReference?> FindEntityAsync(
        AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await FindEntityAsync(
            connection,
            null,
            entityType,
            entityId,
            false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AttachmentSummary>> ListAsync(
        AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        var sql = AttachmentSelect +
            " WHERE a.entity_type=@EntityType AND a.entity_id=@EntityId AND a.deleted_at_utc IS NULL ORDER BY a.uploaded_at_utc DESC,a.id DESC;";
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var rows = await connection.QueryAsync<AttachmentRow>(
            Command(
                sql,
                new { EntityType = entityType.ToString(), EntityId = entityId },
                null,
                cancellationToken));
        return rows.Select(row => row.ToSummary()).ToArray();
    }

    public async Task<AttachmentRecord?> FindAsync(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        return await FindAsync(connection, null, attachmentId, false, cancellationToken);
    }

    public async Task<AttachmentSummary> CreateAsync(
        CreateAttachmentData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var entity = await FindEntityAsync(
            connection,
            transaction,
            data.EntityType,
            data.EntityId,
            true,
            cancellationToken)
            ?? throw new NotFoundException("附件所属业务对象不存在。");
        const string insertSql =
            """
            INSERT INTO attachments
                (entity_type,entity_id,entity_code,original_file_name,storage_key,
                 content_type,size_bytes,sha256,description,version,
                 uploaded_at_utc,uploaded_by_user_id)
            VALUES
                (@EntityType,@EntityId,@EntityCode,@OriginalFileName,@StorageKey,
                 @ContentType,@SizeBytes,@Sha256,@Description,1,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        var attachmentId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    EntityType = data.EntityType.ToString(),
                    data.EntityId,
                    entity.EntityCode,
                    data.OriginalFileName,
                    data.StorageKey,
                    data.ContentType,
                    data.SizeBytes,
                    data.Sha256,
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
            "AttachmentUploaded",
            attachmentId,
            entity.EntityCode,
            $"上传附件 {data.OriginalFileName}",
            null,
            new
            {
                AttachmentId = attachmentId,
                data.EntityType,
                data.EntityId,
                entity.EntityCode,
                data.OriginalFileName,
                data.ContentType,
                data.SizeBytes,
                data.Sha256,
                data.Description,
            },
            data.NowUtc,
            cancellationToken);
        var created = await FindAsync(
            connection,
            transaction,
            attachmentId,
            false,
            cancellationToken)
            ?? throw new InvalidOperationException("Created attachment could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        return created.Summary;
    }

    public async Task DeleteAsync(
        ulong attachmentId,
        DeleteAttachmentData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var existing = await FindAsync(
            connection,
            transaction,
            attachmentId,
            true,
            cancellationToken)
            ?? throw new NotFoundException("附件不存在或已被删除。");
        if (existing.Summary.Version != data.Version)
        {
            throw new ConflictException("附件已被其他操作修改，请刷新后重试。");
        }

        const string updateSql =
            """
            UPDATE attachments
            SET deleted_at_utc=@NowUtc,deleted_by_user_id=@ActorUserId,
                version=version+1
            WHERE id=@AttachmentId AND deleted_at_utc IS NULL AND version=@Version;
            """;
        var changed = await connection.ExecuteAsync(
            Command(
                updateSql,
                new
                {
                    AttachmentId = attachmentId,
                    data.NowUtc,
                    data.ActorUserId,
                    data.Version,
                },
                transaction,
                cancellationToken));
        if (changed != 1)
        {
            throw new ConflictException("附件已被其他操作修改，请刷新后重试。");
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "AttachmentDeleted",
            attachmentId,
            existing.Summary.EntityCode,
            $"删除附件 {existing.Summary.OriginalFileName}",
            existing.Summary,
            new
            {
                Deleted = true,
                DeletedAtUtc = data.NowUtc,
                Version = data.Version + 1,
            },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<AttachmentEntityReference?> FindEntityAsync(
        DbConnection connection,
        DbTransaction? transaction,
        AttachmentEntityType entityType,
        ulong entityId,
        bool forShare,
        CancellationToken cancellationToken)
    {
        var baseSql = entityType switch
        {
            AttachmentEntityType.Customer =>
                "SELECT id AS EntityId,customer_code AS EntityCode FROM customers WHERE id=@EntityId",
            AttachmentEntityType.Opportunity =>
                "SELECT id AS EntityId,opportunity_code AS EntityCode FROM opportunities WHERE id=@EntityId",
            AttachmentEntityType.Project =>
                "SELECT id AS EntityId,project_code AS EntityCode FROM projects WHERE id=@EntityId",
            AttachmentEntityType.Equipment =>
                "SELECT id AS EntityId,equipment_code AS EntityCode FROM equipment WHERE id=@EntityId",
            AttachmentEntityType.ServiceTicket =>
                "SELECT id AS EntityId,service_code AS EntityCode FROM service_tickets WHERE id=@EntityId",
            AttachmentEntityType.Supplier =>
                "SELECT id AS EntityId,supplier_code AS EntityCode FROM suppliers WHERE id=@EntityId",
            AttachmentEntityType.PurchaseOrder =>
                "SELECT id AS EntityId,purchase_order_code AS EntityCode FROM purchase_orders WHERE id=@EntityId",
            AttachmentEntityType.Shipment =>
                "SELECT id AS EntityId,shipment_code AS EntityCode FROM shipments WHERE id=@EntityId",
            _ => throw new ArgumentOutOfRangeException(nameof(entityType)),
        };
        var sql = baseSql + (forShare ? " FOR SHARE;" : ";");
        var row = await connection.QuerySingleOrDefaultAsync<AttachmentEntityRow>(
            Command(sql, new { EntityId = entityId }, transaction, cancellationToken));
        return row is null
            ? null
            : new AttachmentEntityReference(entityType, row.EntityId, row.EntityCode);
    }

    private static async Task<AttachmentRecord?> FindAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong attachmentId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = AttachmentSelect
            + " WHERE a.id=@AttachmentId AND a.deleted_at_utc IS NULL"
            + (forUpdate ? " FOR UPDATE;" : ";");
        var row = await connection.QuerySingleOrDefaultAsync<AttachmentRow>(
            Command(sql, new { AttachmentId = attachmentId }, transaction, cancellationToken));
        return row?.ToRecord();
    }

    private static Task<int> WriteAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        ulong attachmentId,
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
                (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                 entity_code,summary,before_json,after_json)
            VALUES
                (@NowUtc,@ActorUserId,@Action,'Attachment',@AttachmentId,
                 @EntityCode,@Summary,@BeforeJson,@AfterJson);
            """;
        return connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                    Action = action,
                    AttachmentId = attachmentId,
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

    private sealed class AttachmentEntityRow
    {
        public ulong EntityId { get; init; }
        public string? EntityCode { get; init; }
    }

    private sealed class AttachmentRow
    {
        public ulong Id { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public ulong EntityId { get; init; }
        public string? EntityCode { get; init; }
        public string OriginalFileName { get; init; } = string.Empty;
        public string StorageKey { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public ulong SizeBytes { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public string? Description { get; init; }
        public ulong Version { get; init; }
        public DateTime UploadedAtUtc { get; init; }
        public ulong? UploadedByUserId { get; init; }
        public string? UploadedByDisplayName { get; init; }

        public AttachmentSummary ToSummary() =>
            new(
                Id,
                Enum.Parse<AttachmentEntityType>(EntityType, true),
                EntityId,
                EntityCode,
                OriginalFileName,
                ContentType,
                SizeBytes,
                Sha256,
                Description,
                Version,
                UploadedAtUtc,
                UploadedByUserId,
                UploadedByDisplayName);

        public AttachmentRecord ToRecord() => new(ToSummary(), StorageKey);
    }
}
