using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Settings;
using MySqlConnector;

namespace FlowHearth.Infrastructure.Settings;

public sealed class MySqlSettingsRepository(IDbConnectionFactory connectionFactory)
    : ISettingsRepository
{
    public async Task<IReadOnlyList<LookupItemDetails>> ListLookupItemsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT id AS Id,dictionary_code AS DictionaryCode,
                   item_value AS Value,label AS Label,description AS Description,
                   sort_order AS SortOrder,is_active AS IsActive,version AS Version,
                   created_at_utc AS CreatedAtUtc,updated_at_utc AS UpdatedAtUtc
            FROM lookup_items
            WHERE @IncludeInactive=1 OR is_active=1
            ORDER BY dictionary_code,sort_order,label,id;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var rows = await connection.QueryAsync<LookupRow>(
            new CommandDefinition(
                sql,
                new { IncludeInactive = includeInactive ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.Select(row => row.ToDetails()).ToArray();
    }

    public async Task<IReadOnlyList<SystemSettingDetails>> ListSystemSettingsAsync(
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT setting_key AS `Key`,name AS Name,setting_value AS Value,
                   value_type AS ValueType,description AS Description,
                   is_public AS IsPublic,version AS Version,
                   updated_at_utc AS UpdatedAtUtc
            FROM system_settings
            ORDER BY setting_key;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        var rows = await connection.QueryAsync<SettingRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToDetails()).ToArray();
    }

    public async Task<LookupItemDetails> CreateLookupItemAsync(
        LookupItemWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        try
        {
            var id = await connection.QuerySingleAsync<ulong>(
                new CommandDefinition(
                    """
                    INSERT INTO lookup_items
                        (dictionary_code,item_value,label,description,sort_order,
                         is_active,version,created_at_utc,created_by_user_id,
                         updated_at_utc,updated_by_user_id)
                    VALUES
                        (@DictionaryCode,@Value,@Label,@Description,@SortOrder,
                         1,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
                    SELECT LAST_INSERT_ID();
                    """,
                    data,
                    transaction,
                    cancellationToken: cancellationToken));
            var created = await GetLookupAsync(
                connection,
                transaction,
                id,
                cancellationToken)
                ?? throw new InvalidOperationException("Created lookup item was not found.");
            await InsertAuditAsync(
                connection,
                transaction,
                data.ActorUserId,
                "LookupItemCreated",
                "LookupItem",
                id,
                $"{created.DictionaryCode}:{created.Value}",
                $"新增字典项 {created.Label}",
                null,
                JsonSerializer.Serialize(created),
                data.NowUtc,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("该字典值已存在。");
        }
    }

    public async Task<LookupItemDetails> UpdateLookupItemAsync(
        ulong itemId,
        LookupItemWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var before = await GetLookupAsync(
            connection,
            transaction,
            itemId,
            cancellationToken,
            forUpdate: true)
            ?? throw new NotFoundException("字典项不存在。");
        if (before.Version != data.Version)
        {
            throw new ConflictException("字典项已被其他操作修改，请刷新后重试。");
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE lookup_items
                SET label=@Label,description=@Description,sort_order=@SortOrder,
                    is_active=@IsActive,version=version+1,
                    updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
                WHERE id=@ItemId AND version=@Version;
                """,
                new
                {
                    ItemId = itemId,
                    data.Label,
                    data.Description,
                    data.SortOrder,
                    IsActive = data.IsActive ? 1 : 0,
                    data.NowUtc,
                    data.ActorUserId,
                    data.Version,
                },
                transaction,
                cancellationToken: cancellationToken));
        var updated = await GetLookupAsync(
            connection,
            transaction,
            itemId,
            cancellationToken)
            ?? throw new InvalidOperationException("Updated lookup item was not found.");
        await InsertAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "LookupItemUpdated",
            "LookupItem",
            itemId,
            $"{updated.DictionaryCode}:{updated.Value}",
            $"修改字典项 {updated.Label}",
            JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(updated),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<SystemSettingDetails> UpdateSystemSettingAsync(
        SystemSettingWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var before = await GetSettingAsync(
            connection,
            transaction,
            data.Key,
            cancellationToken,
            forUpdate: true)
            ?? throw new NotFoundException("系统设置不存在。");
        if (before.Version != data.Version)
        {
            throw new ConflictException("系统设置已被其他操作修改，请刷新后重试。");
        }

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE system_settings
                SET setting_value=@Value,version=version+1,
                    updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
                WHERE setting_key=@Key AND version=@Version;
                """,
                data,
                transaction,
                cancellationToken: cancellationToken));
        var updated = await GetSettingAsync(
            connection,
            transaction,
            data.Key,
            cancellationToken)
            ?? throw new InvalidOperationException("Updated system setting was not found.");
        await InsertAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "SystemSettingUpdated",
            "SystemSetting",
            0,
            updated.Key,
            $"修改系统设置 {updated.Name}",
            JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(updated),
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private static async Task<LookupItemDetails?> GetLookupAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong itemId,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql =
            """
            SELECT id AS Id,dictionary_code AS DictionaryCode,
                   item_value AS Value,label AS Label,description AS Description,
                   sort_order AS SortOrder,is_active AS IsActive,version AS Version,
                   created_at_utc AS CreatedAtUtc,updated_at_utc AS UpdatedAtUtc
            FROM lookup_items
            WHERE id=@ItemId
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var row = await connection.QuerySingleOrDefaultAsync<LookupRow>(
            new CommandDefinition(
                sql,
                new { ItemId = itemId },
                transaction,
                cancellationToken: cancellationToken));
        return row?.ToDetails();
    }

    private static async Task<SystemSettingDetails?> GetSettingAsync(
        DbConnection connection,
        DbTransaction transaction,
        string key,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql =
            """
            SELECT setting_key AS `Key`,name AS Name,setting_value AS Value,
                   value_type AS ValueType,description AS Description,
                   is_public AS IsPublic,version AS Version,
                   updated_at_utc AS UpdatedAtUtc
            FROM system_settings
            WHERE setting_key=@Key
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var row = await connection.QuerySingleOrDefaultAsync<SettingRow>(
            new CommandDefinition(
                sql,
                new { Key = key },
                transaction,
                cancellationToken: cancellationToken));
        return row?.ToDetails();
    }

    private static Task<int> InsertAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        string entityType,
        ulong entityId,
        string entityCode,
        string summary,
        string? beforeJson,
        string? afterJson,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO audit_logs
                    (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                     entity_code,summary,before_json,after_json)
                VALUES
                    (@NowUtc,@ActorUserId,@Action,@EntityType,@EntityId,
                     @EntityCode,@Summary,@BeforeJson,@AfterJson);
                """,
                new
                {
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    EntityCode = entityCode,
                    Summary = summary,
                    BeforeJson = beforeJson,
                    AfterJson = afterJson,
                },
                transaction,
                cancellationToken: cancellationToken));

    private sealed class LookupRow
    {
        public ulong Id { get; init; }
        public required string DictionaryCode { get; init; }
        public required string Value { get; init; }
        public required string Label { get; init; }
        public string? Description { get; init; }
        public int SortOrder { get; init; }
        public long IsActive { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public LookupItemDetails ToDetails() => new(
            Id,
            DictionaryCode,
            Value,
            Label,
            Description,
            SortOrder,
            IsActive != 0,
            Version,
            CreatedAtUtc,
            UpdatedAtUtc);
    }

    private sealed class SettingRow
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Value { get; init; }
        public required string ValueType { get; init; }
        public string? Description { get; init; }
        public long IsPublic { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public SystemSettingDetails ToDetails() => new(
            Key,
            Name,
            Value,
            Enum.Parse<SystemSettingValueType>(ValueType, true),
            Description,
            IsPublic != 0,
            Version,
            UpdatedAtUtc);
    }
}
