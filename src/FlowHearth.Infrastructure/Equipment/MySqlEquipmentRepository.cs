using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Equipment;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Equipment;

public sealed class MySqlEquipmentRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null)
    : IEquipmentRepository
{
    public async Task<PagedResult<EquipmentSummary>> ListAsync(
        EquipmentListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var archiveClause = criteria.ArchiveMode switch
        {
            EquipmentArchiveMode.Active => "e.archived_at_utc IS NULL",
            EquipmentArchiveMode.Archived => "e.archived_at_utc IS NOT NULL",
            EquipmentArchiveMode.All => "1 = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var orderBy = criteria.SortBy.ToLowerInvariant() switch
        {
            "code" => "e.equipment_code",
            "name" => "e.name",
            "category" => "e.category",
            "customer" => "c.name",
            "project" => "p.name",
            "commissioneddate" => "e.commissioned_date",
            "updatedat" => "e.updated_at_utc",
            _ => throw new ArgumentOutOfRangeException(nameof(criteria)),
        };
        var direction = criteria.SortDescending ? "DESC" : "ASC";
        var sql = $"""
            SELECT COUNT(*)
            FROM equipment e
            INNER JOIN customers c ON c.id=e.customer_id
            LEFT JOIN projects p ON p.id=e.project_id
            WHERE {archiveClause}
              AND (@Category IS NULL OR e.category=@Category)
              AND (@CustomerId IS NULL OR e.customer_id=@CustomerId)
              AND (@ProjectId IS NULL OR e.project_id=@ProjectId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(e.equipment_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR e.name LIKE @SearchPattern ESCAPE '='
                   OR e.manufacturer LIKE @SearchPattern ESCAPE '='
                   OR e.model LIKE @SearchPattern ESCAPE '='
                   OR e.serial_number LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR p.name LIKE @SearchPattern ESCAPE '=');

            SELECT e.id AS Id, e.equipment_code AS Code,
                   e.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   c.name AS CustomerName, e.project_id AS ProjectId,
                   p.project_code AS ProjectCode, p.name AS ProjectName,
                   e.name AS Name, e.category AS Category,
                   e.manufacturer AS Manufacturer, e.model AS Model,
                   e.serial_number AS SerialNumber,
                   e.archived_at_utc AS ArchivedAtUtc,
                   (SELECT COUNT(*) FROM equipment_components ec
                    WHERE ec.equipment_id=e.id AND ec.deleted_at_utc IS NULL) AS ComponentCount,
                   (SELECT COUNT(*) FROM equipment_parameters ep
                    WHERE ep.equipment_id=e.id AND ep.deleted_at_utc IS NULL) AS ParameterCount,
                   (SELECT COUNT(*) FROM equipment_versions ev
                    WHERE ev.equipment_id=e.id AND ev.deleted_at_utc IS NULL) AS VersionCount,
                   e.version AS Version, e.updated_at_utc AS UpdatedAtUtc
            FROM equipment e
            INNER JOIN customers c ON c.id=e.customer_id
            LEFT JOIN projects p ON p.id=e.project_id
            WHERE {archiveClause}
              AND (@Category IS NULL OR e.category=@Category)
              AND (@CustomerId IS NULL OR e.customer_id=@CustomerId)
              AND (@ProjectId IS NULL OR e.project_id=@ProjectId)
              AND (@SearchPattern IS NULL
                   OR CONVERT(e.equipment_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR e.name LIKE @SearchPattern ESCAPE '='
                   OR e.manufacturer LIKE @SearchPattern ESCAPE '='
                   OR e.model LIKE @SearchPattern ESCAPE '='
                   OR e.serial_number LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(c.customer_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR p.name LIKE @SearchPattern ESCAPE '=')
            ORDER BY {orderBy} {direction}, e.id {direction}
            LIMIT @PageSize OFFSET @Offset;
            """;
        var parameters = new
        {
            Category = criteria.Category?.ToString(),
            criteria.CustomerId,
            criteria.ProjectId,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(Command(sql, parameters, null, cancellationToken));
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<EquipmentSummaryRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        return new PagedResult<EquipmentSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<EquipmentDetails?> GetAsync(
        ulong equipmentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, equipmentId, cancellationToken);
    }

    public async Task<EquipmentDetails> CreateAsync(
        EquipmentWriteData data,
        CancellationToken cancellationToken)
    {
        const string sequenceSql =
            """
            INSERT INTO number_sequences (sequence_name, current_value, updated_at_utc)
            VALUES (@SequenceName, LAST_INSERT_ID(1), @NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value=LAST_INSERT_ID(current_value+1),
                updated_at_utc=VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO equipment
                (equipment_code, customer_id, project_id, name, category,
                 manufacturer, model, serial_number, install_location,
                 commissioned_date, notes, version, created_at_utc,
                 created_by_user_id, updated_at_utc, updated_by_user_id)
            VALUES
                (@Code, @CustomerId, @ProjectId, @Name, @Category,
                 @Manufacturer, @Model, @SerialNumber, @InstallLocation,
                 @CommissionedDate, @Notes, 1, @NowUtc,
                 @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var links = await RequireActiveLinksAsync(
            connection,
            transaction,
            data.CustomerId,
            data.ProjectId,
            cancellationToken);
        var sequence = await connection.QuerySingleAsync<ulong>(
            Command(
                sequenceSql,
                new { SequenceName = $"equipment:{data.NowUtc.Year:D4}", data.NowUtc },
                transaction,
                cancellationToken));
        var prefix = runtimeSettingsProvider is null
            ? EquipmentCode.DefaultPrefix
            : (await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken))
                .NumberPrefixes.Equipment;
        var code = EquipmentCode.Format(prefix, data.NowUtc.Year, sequence);
        var equipmentId = await connection.QuerySingleAsync<ulong>(
            Command(
                insertSql,
                new
                {
                    Code = code,
                    data.CustomerId,
                    data.ProjectId,
                    data.Name,
                    Category = data.Category.ToString(),
                    data.Manufacturer,
                    data.Model,
                    data.SerialNumber,
                    data.InstallLocation,
                    data.CommissionedDate,
                    data.Notes,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "equipment.created",
            "equipment",
            equipmentId,
            code,
            $"为客户 {links.CustomerName} 创建设备 {data.Name}",
            null,
            new { data.CustomerId, data.ProjectId, data.Name, data.Category },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(equipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Created equipment could not be loaded.");
    }

    public async Task<EquipmentDetails?> UpdateAsync(
        ulong equipmentId,
        EquipmentWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE equipment
            SET customer_id=@CustomerId, project_id=@ProjectId, name=@Name,
                category=@Category, manufacturer=@Manufacturer, model=@Model,
                serial_number=@SerialNumber, install_location=@InstallLocation,
                commissioned_date=@CommissionedDate, notes=@Notes,
                version=version+1, updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@EquipmentId AND version=@Version AND archived_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, equipmentId, cancellationToken);
        if (before is null || before.ArchivedAtUtc.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        _ = await RequireActiveLinksAsync(connection, transaction, data.CustomerId, data.ProjectId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    EquipmentId = equipmentId,
                    data.CustomerId,
                    data.ProjectId,
                    data.Name,
                    Category = data.Category.ToString(),
                    data.Manufacturer,
                    data.Model,
                    data.SerialNumber,
                    data.InstallLocation,
                    data.CommissionedDate,
                    data.Notes,
                    data.Version,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            "equipment.updated",
            "equipment",
            equipmentId,
            before.Code,
            $"更新设备 {data.Name}",
            before,
            new { data.CustomerId, data.ProjectId, data.Name, data.Category, data.SerialNumber },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(equipmentId, cancellationToken);
    }

    public async Task<EquipmentDetails?> SetArchivedAsync(
        ulong equipmentId,
        SetEquipmentArchiveData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE equipment
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version+1, updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@EquipmentId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL));
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await GetSnapshotAsync(connection, transaction, equipmentId, cancellationToken);
        if (before is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    EquipmentId = equipmentId,
                    data.Archived,
                    data.Version,
                    data.ActorUserId,
                    data.NowUtc,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await WriteAuditAsync(
            connection,
            transaction,
            data.ActorUserId,
            data.Archived ? "equipment.archived" : "equipment.restored",
            "equipment",
            equipmentId,
            before.Code,
            data.Archived ? $"归档设备 {before.Name}" : $"恢复设备 {before.Name}",
            new { Archived = before.ArchivedAtUtc.HasValue },
            new { data.Archived },
            data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(equipmentId, cancellationToken);
    }

    public async Task<EquipmentComponentDetails> CreateComponentAsync(
        ulong equipmentId,
        EquipmentComponentWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO equipment_components
                (equipment_id, category, name, manufacturer, model, serial_number,
                 firmware_version, quantity, install_location, notes, sort_order,
                 version, created_at_utc, created_by_user_id, updated_at_utc,
                 updated_by_user_id)
            VALUES
                (@EquipmentId, @Category, @Name, @Manufacturer, @Model,
                 @SerialNumber, @FirmwareVersion, @Quantity, @InstallLocation,
                 @Notes, @SortOrder, 1, @NowUtc, @ActorUserId, @NowUtc,
                 @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var componentId = await connection.QuerySingleAsync<ulong>(
            Command(sql, ComponentParameters(equipmentId, data), transaction, cancellationToken));
        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_component.created", "equipment_component", componentId, equipment.Code, $"为设备 {equipment.Name} 添加组件 {data.Name}", null, new { equipmentId, data.Category, data.Name, data.Model, data.Quantity }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetComponentAsync(connection, null, equipmentId, componentId, cancellationToken)
            ?? throw new InvalidOperationException("Created equipment component could not be loaded.");
    }

    public async Task<EquipmentComponentDetails?> UpdateComponentAsync(
        ulong equipmentId,
        ulong componentId,
        EquipmentComponentWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE equipment_components
            SET category=@Category, name=@Name, manufacturer=@Manufacturer,
                model=@Model, serial_number=@SerialNumber,
                firmware_version=@FirmwareVersion, quantity=@Quantity,
                install_location=@InstallLocation, notes=@Notes,
                sort_order=@SortOrder, version=version+1,
                updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId
            WHERE id=@ComponentId AND equipment_id=@EquipmentId
              AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var before = await GetComponentAsync(connection, transaction, equipmentId, componentId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, ComponentParameters(equipmentId, data, componentId), transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_component.updated", "equipment_component", componentId, equipment.Code, $"更新设备 {equipment.Name} 组件 {data.Name}", before, new { data.Category, data.Name, data.Model, data.Quantity }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetComponentAsync(connection, null, equipmentId, componentId, cancellationToken);
    }

    public Task<bool> DeleteComponentAsync(
        ulong equipmentId,
        ulong componentId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        DeleteChildAsync(
            "equipment_components",
            "equipment_component.deleted",
            "equipment_component",
            "组件",
            equipmentId,
            componentId,
            version,
            actorUserId,
            nowUtc,
            cancellationToken);

    public async Task<EquipmentParameterDetails> CreateParameterAsync(
        ulong equipmentId,
        EquipmentParameterWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO equipment_parameters
                (equipment_id, parameter_group, name, `value`, unit, notes,
                 sort_order, version, created_at_utc, created_by_user_id,
                 updated_at_utc, updated_by_user_id)
            VALUES
                (@EquipmentId, @ParameterGroup, @Name, @Value, @Unit, @Notes,
                 @SortOrder, 1, @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var parameterId = await connection.QuerySingleAsync<ulong>(
            Command(sql, ParameterParameters(equipmentId, data), transaction, cancellationToken));
        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_parameter.created", "equipment_parameter", parameterId, equipment.Code, $"为设备 {equipment.Name} 添加参数 {data.Name}", null, new { equipmentId, data.ParameterGroup, data.Name, data.Value, data.Unit }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetParameterAsync(connection, null, equipmentId, parameterId, cancellationToken)
            ?? throw new InvalidOperationException("Created equipment parameter could not be loaded.");
    }

    public async Task<EquipmentParameterDetails?> UpdateParameterAsync(
        ulong equipmentId,
        ulong parameterId,
        EquipmentParameterWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE equipment_parameters
            SET parameter_group=@ParameterGroup, name=@Name, `value`=@Value,
                unit=@Unit, notes=@Notes, sort_order=@SortOrder,
                version=version+1, updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@ParameterId AND equipment_id=@EquipmentId
              AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var before = await GetParameterAsync(connection, transaction, equipmentId, parameterId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, ParameterParameters(equipmentId, data, parameterId), transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_parameter.updated", "equipment_parameter", parameterId, equipment.Code, $"更新设备 {equipment.Name} 参数 {data.Name}", before, new { data.ParameterGroup, data.Name, data.Value, data.Unit }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetParameterAsync(connection, null, equipmentId, parameterId, cancellationToken);
    }

    public Task<bool> DeleteParameterAsync(
        ulong equipmentId,
        ulong parameterId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        DeleteChildAsync(
            "equipment_parameters",
            "equipment_parameter.deleted",
            "equipment_parameter",
            "参数",
            equipmentId,
            parameterId,
            version,
            actorUserId,
            nowUtc,
            cancellationToken);

    public async Task<EquipmentVersionDetails> CreateVersionAsync(
        ulong equipmentId,
        EquipmentVersionWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO equipment_versions
                (equipment_id, version_type, version_label, git_commit,
                 changelog, released_date, notes, version, created_at_utc,
                 created_by_user_id, updated_at_utc, updated_by_user_id)
            VALUES
                (@EquipmentId, @VersionType, @VersionLabel, @GitCommit,
                 @Changelog, @ReleasedDate, @Notes, 1, @NowUtc,
                 @ActorUserId, @NowUtc, @ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var versionId = await connection.QuerySingleAsync<ulong>(
            Command(sql, VersionParameters(equipmentId, data), transaction, cancellationToken));
        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_version.created", "equipment_version", versionId, equipment.Code, $"为设备 {equipment.Name} 添加版本 {data.VersionLabel}", null, new { equipmentId, data.VersionType, data.VersionLabel, data.GitCommit, data.Changelog }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetVersionAsync(connection, null, equipmentId, versionId, cancellationToken)
            ?? throw new InvalidOperationException("Created equipment version could not be loaded.");
    }

    public async Task<EquipmentVersionDetails?> UpdateVersionAsync(
        ulong equipmentId,
        ulong versionId,
        EquipmentVersionWriteData data,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            UPDATE equipment_versions
            SET version_type=@VersionType, version_label=@VersionLabel,
                git_commit=@GitCommit, changelog=@Changelog,
                released_date=@ReleasedDate, notes=@Notes,
                version=version+1, updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@VersionId AND equipment_id=@EquipmentId
              AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var before = await GetVersionAsync(connection, transaction, equipmentId, versionId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, VersionParameters(equipmentId, data, versionId), transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await TouchEquipmentAsync(connection, transaction, equipmentId, data.ActorUserId, data.NowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, data.ActorUserId, "equipment_version.updated", "equipment_version", versionId, equipment.Code, $"更新设备 {equipment.Name} 版本 {data.VersionLabel}", before, new { data.VersionType, data.VersionLabel, data.GitCommit, data.Changelog }, data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetVersionAsync(connection, null, equipmentId, versionId, cancellationToken);
    }

    public Task<bool> DeleteVersionAsync(
        ulong equipmentId,
        ulong versionId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        DeleteChildAsync(
            "equipment_versions",
            "equipment_version.deleted",
            "equipment_version",
            "版本",
            equipmentId,
            versionId,
            version,
            actorUserId,
            nowUtc,
            cancellationToken);

    private async Task<bool> DeleteChildAsync(
        string table,
        string action,
        string entityType,
        string childLabel,
        ulong equipmentId,
        ulong childId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {table}
            SET deleted_at_utc=@NowUtc, deleted_by_user_id=@ActorUserId,
                version=version+1, updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@ChildId AND equipment_id=@EquipmentId
              AND version=@Version AND deleted_at_utc IS NULL;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var equipment = await RequireMutableEquipmentAsync(connection, transaction, equipmentId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, new { ChildId = childId, EquipmentId = equipmentId, Version = version, ActorUserId = actorUserId, NowUtc = nowUtc }, transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await TouchEquipmentAsync(connection, transaction, equipmentId, actorUserId, nowUtc, cancellationToken);
        await WriteAuditAsync(connection, transaction, actorUserId, action, entityType, childId, equipment.Code, $"删除设备 {equipment.Name} {childLabel}", null, null, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<EquipmentDetails?> GetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong equipmentId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT e.id AS Id, e.equipment_code AS Code,
                   e.customer_id AS CustomerId, c.customer_code AS CustomerCode,
                   c.name AS CustomerName, e.project_id AS ProjectId,
                   p.project_code AS ProjectCode, p.name AS ProjectName,
                   e.name AS Name, e.category AS Category,
                   e.manufacturer AS Manufacturer, e.model AS Model,
                   e.serial_number AS SerialNumber,
                   e.install_location AS InstallLocation,
                   e.commissioned_date AS CommissionedDate,
                   e.notes AS Notes, e.archived_at_utc AS ArchivedAtUtc,
                   e.version AS Version, e.created_at_utc AS CreatedAtUtc,
                   e.updated_at_utc AS UpdatedAtUtc
            FROM equipment e
            INNER JOIN customers c ON c.id=e.customer_id
            LEFT JOIN projects p ON p.id=e.project_id
            WHERE e.id=@EquipmentId;

            SELECT id AS Id, equipment_id AS EquipmentId, category AS Category,
                   name AS Name, manufacturer AS Manufacturer, model AS Model,
                   serial_number AS SerialNumber,
                   firmware_version AS FirmwareVersion, quantity AS Quantity,
                   install_location AS InstallLocation, notes AS Notes,
                   sort_order AS SortOrder, version AS Version,
                   created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
            FROM equipment_components
            WHERE equipment_id=@EquipmentId AND deleted_at_utc IS NULL
            ORDER BY sort_order, id;

            SELECT id AS Id, equipment_id AS EquipmentId,
                   parameter_group AS ParameterGroup, name AS Name,
                   `value` AS Value, unit AS Unit, notes AS Notes,
                   sort_order AS SortOrder, version AS Version,
                   created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc
            FROM equipment_parameters
            WHERE equipment_id=@EquipmentId AND deleted_at_utc IS NULL
            ORDER BY parameter_group, sort_order, id;

            SELECT id AS Id, equipment_id AS EquipmentId,
                   version_type AS VersionType, version_label AS VersionLabel,
                   git_commit AS GitCommit, changelog AS Changelog,
                   released_date AS ReleasedDate, notes AS Notes,
                   version AS Version, created_at_utc AS CreatedAtUtc,
                   updated_at_utc AS UpdatedAtUtc
            FROM equipment_versions
            WHERE equipment_id=@EquipmentId AND deleted_at_utc IS NULL
            ORDER BY released_date DESC, id DESC;
            """;
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new { EquipmentId = equipmentId }, transaction, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<EquipmentHeader>();
        if (header is null)
        {
            return null;
        }

        var components = (await result.ReadAsync<EquipmentComponentRow>())
            .Select(row => row.ToDetails())
            .ToArray();
        var parameters = (await result.ReadAsync<EquipmentParameterDetails>()).AsList();
        var versions = (await result.ReadAsync<EquipmentVersionDetails>()).AsList();
        return header.ToDetails(components, parameters, versions);
    }

    private static Task<EquipmentSnapshot?> GetSnapshotAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong equipmentId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<EquipmentSnapshot>(
            Command(
                "SELECT id AS Id, equipment_code AS Code, name AS Name, archived_at_utc AS ArchivedAtUtc, version AS Version FROM equipment WHERE id=@EquipmentId FOR UPDATE;",
                new { EquipmentId = equipmentId },
                transaction,
                cancellationToken));

    private static async Task<EquipmentSnapshot> RequireMutableEquipmentAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong equipmentId,
        CancellationToken cancellationToken)
    {
        var equipment = await GetSnapshotAsync(connection, transaction, equipmentId, cancellationToken)
            ?? throw new NotFoundException("设备不存在。");
        if (equipment.ArchivedAtUtc.HasValue)
        {
            throw new ConflictException("归档设备不能修改技术档案。");
        }

        return equipment;
    }

    private static async Task<LinkReferences> RequireActiveLinksAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        ulong? projectId,
        CancellationToken cancellationToken)
    {
        var customer = await connection.QuerySingleOrDefaultAsync<NamedReference>(
            Command(
                "SELECT id AS Id, name AS Name FROM customers WHERE id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;",
                new { CustomerId = customerId },
                transaction,
                cancellationToken))
            ?? throw new NotFoundException("客户不存在或已经归档。");
        if (!projectId.HasValue)
        {
            return new LinkReferences(customer.Name, null);
        }

        var project = await connection.QuerySingleOrDefaultAsync<NamedReference>(
            Command(
                "SELECT id AS Id, name AS Name FROM projects WHERE id=@ProjectId AND customer_id=@CustomerId AND archived_at_utc IS NULL FOR UPDATE;",
                new { ProjectId = projectId.Value, CustomerId = customerId },
                transaction,
                cancellationToken))
            ?? throw new NotFoundException("项目不存在、已经归档或不属于所选客户。");
        return new LinkReferences(customer.Name, project.Name);
    }

    private static Task<EquipmentComponentDetails?> GetComponentAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong equipmentId,
        ulong componentId,
        CancellationToken cancellationToken) =>
        GetComponentRowAsync(connection, transaction, equipmentId, componentId, cancellationToken);

    private static async Task<EquipmentComponentDetails?> GetComponentRowAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong equipmentId,
        ulong componentId,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<EquipmentComponentRow>(
            Command(
                "SELECT id AS Id, equipment_id AS EquipmentId, category AS Category, name AS Name, manufacturer AS Manufacturer, model AS Model, serial_number AS SerialNumber, firmware_version AS FirmwareVersion, quantity AS Quantity, install_location AS InstallLocation, notes AS Notes, sort_order AS SortOrder, version AS Version, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc FROM equipment_components WHERE id=@ComponentId AND equipment_id=@EquipmentId AND deleted_at_utc IS NULL;",
                new { ComponentId = componentId, EquipmentId = equipmentId },
                transaction,
                cancellationToken));
        return row?.ToDetails();
    }

    private static Task<EquipmentParameterDetails?> GetParameterAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong equipmentId,
        ulong parameterId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<EquipmentParameterDetails>(
            Command(
                "SELECT id AS Id, equipment_id AS EquipmentId, parameter_group AS ParameterGroup, name AS Name, `value` AS Value, unit AS Unit, notes AS Notes, sort_order AS SortOrder, version AS Version, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc FROM equipment_parameters WHERE id=@ParameterId AND equipment_id=@EquipmentId AND deleted_at_utc IS NULL;",
                new { ParameterId = parameterId, EquipmentId = equipmentId },
                transaction,
                cancellationToken));

    private static Task<EquipmentVersionDetails?> GetVersionAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong equipmentId,
        ulong versionId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<EquipmentVersionDetails>(
            Command(
                "SELECT id AS Id, equipment_id AS EquipmentId, version_type AS VersionType, version_label AS VersionLabel, git_commit AS GitCommit, changelog AS Changelog, released_date AS ReleasedDate, notes AS Notes, version AS Version, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc FROM equipment_versions WHERE id=@VersionId AND equipment_id=@EquipmentId AND deleted_at_utc IS NULL;",
                new { VersionId = versionId, EquipmentId = equipmentId },
                transaction,
                cancellationToken));

    private static object ComponentParameters(
        ulong equipmentId,
        EquipmentComponentWriteData data,
        ulong? componentId = null) =>
        new
        {
            EquipmentId = equipmentId,
            ComponentId = componentId,
            Category = data.Category.ToString(),
            data.Name,
            data.Manufacturer,
            data.Model,
            data.SerialNumber,
            data.FirmwareVersion,
            data.Quantity,
            data.InstallLocation,
            data.Notes,
            data.SortOrder,
            data.Version,
            data.ActorUserId,
            data.NowUtc,
        };

    private static object ParameterParameters(
        ulong equipmentId,
        EquipmentParameterWriteData data,
        ulong? parameterId = null) =>
        new
        {
            EquipmentId = equipmentId,
            ParameterId = parameterId,
            data.ParameterGroup,
            data.Name,
            data.Value,
            data.Unit,
            data.Notes,
            data.SortOrder,
            data.Version,
            data.ActorUserId,
            data.NowUtc,
        };

    private static object VersionParameters(
        ulong equipmentId,
        EquipmentVersionWriteData data,
        ulong? versionId = null) =>
        new
        {
            EquipmentId = equipmentId,
            VersionId = versionId,
            data.VersionType,
            data.VersionLabel,
            data.GitCommit,
            data.Changelog,
            data.ReleasedDate,
            data.Notes,
            data.Version,
            data.ActorUserId,
            data.NowUtc,
        };

    private static Task<int> TouchEquipmentAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong equipmentId,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            Command(
                "UPDATE equipment SET updated_at_utc=@NowUtc, updated_by_user_id=@ActorUserId WHERE id=@EquipmentId;",
                new { EquipmentId = equipmentId, ActorUserId = actorUserId, NowUtc = nowUtc },
                transaction,
                cancellationToken));

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

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private sealed class EquipmentSummaryRow
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string CustomerName { get; init; }
        public ulong? ProjectId { get; init; }
        public string? ProjectCode { get; init; }
        public string? ProjectName { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public string? SerialNumber { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public int ComponentCount { get; init; }
        public int ParameterCount { get; init; }
        public int VersionCount { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public EquipmentSummary ToDetails() =>
            new(Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode, ProjectName, Name, Enum.Parse<EquipmentCategory>(Category, true), Manufacturer, Model, SerialNumber, ArchivedAtUtc.HasValue, ComponentCount, ParameterCount, VersionCount, Version, UpdatedAtUtc);
    }

    private sealed class EquipmentHeader
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public ulong CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public required string CustomerName { get; init; }
        public ulong? ProjectId { get; init; }
        public string? ProjectCode { get; init; }
        public string? ProjectName { get; init; }
        public required string Name { get; init; }
        public required string Category { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public string? SerialNumber { get; init; }
        public string? InstallLocation { get; init; }
        public DateTime? CommissionedDate { get; init; }
        public string? Notes { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public EquipmentDetails ToDetails(
            IReadOnlyList<EquipmentComponentDetails> components,
            IReadOnlyList<EquipmentParameterDetails> parameters,
            IReadOnlyList<EquipmentVersionDetails> versions) =>
            new(Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode, ProjectName, Name, Enum.Parse<EquipmentCategory>(Category, true), Manufacturer, Model, SerialNumber, InstallLocation, CommissionedDate, Notes, ArchivedAtUtc.HasValue, ArchivedAtUtc, Version, CreatedAtUtc, UpdatedAtUtc, components, parameters, versions);
    }

    private sealed class EquipmentComponentRow
    {
        public ulong Id { get; init; }
        public ulong EquipmentId { get; init; }
        public required string Category { get; init; }
        public required string Name { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public string? SerialNumber { get; init; }
        public string? FirmwareVersion { get; init; }
        public uint Quantity { get; init; }
        public string? InstallLocation { get; init; }
        public string? Notes { get; init; }
        public int SortOrder { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public EquipmentComponentDetails ToDetails() =>
            new(Id, EquipmentId, Enum.Parse<EquipmentCategory>(Category, true), Name, Manufacturer, Model, SerialNumber, FirmwareVersion, Quantity, InstallLocation, Notes, SortOrder, Version, CreatedAtUtc, UpdatedAtUtc);
    }

    private sealed class EquipmentSnapshot
    {
        public ulong Id { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class NamedReference
    {
        public ulong Id { get; init; }
        public required string Name { get; init; }
    }

    private sealed record LinkReferences(string CustomerName, string? ProjectName);
}
