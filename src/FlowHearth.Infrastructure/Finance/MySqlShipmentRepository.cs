using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Infrastructure.Database;
using MySqlConnector;

namespace FlowHearth.Infrastructure.Finance;

public sealed class MySqlShipmentRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IShipmentRepository
{
    private const string SummarySelect =
        """
        SELECT sh.id AS Id,sh.shipment_code AS Code,
               sh.customer_id AS CustomerId,c.customer_code AS CustomerCode,c.name AS CustomerName,
               sh.project_id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
               sh.shipment_date AS ShipmentDate,sh.status AS Status,
               sh.receiver_name AS ReceiverName,sh.logistics_company AS LogisticsCompany,
               sh.tracking_number AS TrackingNumber,sh.signed_at_utc AS SignedAtUtc,
               sh.receiver_mobile AS ReceiverMobile,
               sh.shipping_address AS ShippingAddress,sh.remark AS Remark,
               sh.archived_at_utc AS ArchivedAtUtc,sh.created_at_utc AS CreatedAtUtc,
               COALESCE(ic.ItemCount,0) AS ItemCount,
               COALESCE(ic.EquipmentCount,0) AS EquipmentCount,
               (sh.archived_at_utc IS NOT NULL) AS IsArchived,
               sh.version AS Version,sh.updated_at_utc AS UpdatedAtUtc
        FROM shipments sh
        INNER JOIN customers c ON c.id=sh.customer_id
        INNER JOIN projects p ON p.id=sh.project_id
        LEFT JOIN
        (
            SELECT shipment_id,COUNT(*) AS ItemCount,
                   SUM(CASE WHEN equipment_id IS NULL THEN 0 ELSE 1 END) AS EquipmentCount
            FROM shipment_items
            WHERE deleted_at_utc IS NULL
            GROUP BY shipment_id
        ) ic ON ic.shipment_id=sh.id
        """;

    public async Task<PagedResult<ShipmentSummary>> ListAsync(
        ShipmentListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var where =
            """
            WHERE (@ArchiveMode=2
                   OR (@ArchiveMode=0 AND sh.archived_at_utc IS NULL)
                   OR (@ArchiveMode=1 AND sh.archived_at_utc IS NOT NULL))
              AND (@CustomerId IS NULL OR sh.customer_id=@CustomerId)
              AND (@ProjectId IS NULL OR sh.project_id=@ProjectId)
              AND (@Status IS NULL OR sh.status=@Status)
              AND (@ShipmentFrom IS NULL OR sh.shipment_date>=@ShipmentFrom)
              AND (@ShipmentTo IS NULL OR sh.shipment_date<=@ShipmentTo)
              AND (@IsReceived IS NULL
                   OR (@IsReceived=1 AND sh.status='Received')
                   OR (@IsReceived=0 AND sh.status<>'Received'))
              AND (@SearchPattern IS NULL
                   OR CONVERT(sh.shipment_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR c.name LIKE @SearchPattern ESCAPE '='
                   OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR p.name LIKE @SearchPattern ESCAPE '='
                   OR sh.tracking_number LIKE @SearchPattern ESCAPE '='
                   OR sh.receiver_name LIKE @SearchPattern ESCAPE '=')
            """;
        var parameters = new
        {
            ArchiveMode = (int)criteria.ArchiveMode,
            criteria.CustomerId,
            criteria.ProjectId,
            Status = criteria.Status?.ToString(),
            criteria.ShipmentFrom,
            criteria.ShipmentTo,
            IsReceived = criteria.IsReceived.HasValue
                ? criteria.IsReceived.Value ? 1 : 0
                : (int?)null,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        var sql = $"{SummarySelect} {where} ORDER BY {OrderBy(criteria.SortBy, criteria.SortDescending)} LIMIT @PageSize OFFSET @Offset; SELECT COUNT(*) FROM shipments sh INNER JOIN customers c ON c.id=sh.customer_id INNER JOIN projects p ON p.id=sh.project_id {where};";
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<ShipmentRow>())
            .Select(row => row.ToSummary()).ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<ShipmentDetails?> GetAsync(
        ulong shipmentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, shipmentId, cancellationToken);
    }

    public async Task<ShipmentDetails> CreateAsync(
        ShipmentWriteData data,
        CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await ValidateOwnersAndEquipmentAsync(connection, transaction, data, cancellationToken);
        var sequence = await NextSequenceAsync(connection, transaction,
            $"shipment:{data.NowUtc.Year:D4}", data.NowUtc, cancellationToken);
        var code = FinanceCodes.Format(runtime.NumberPrefixes.Shipment, data.NowUtc.Year, sequence);
        var id = await connection.QuerySingleAsync<ulong>(Command(
            """
            INSERT INTO shipments
                (shipment_code,customer_id,project_id,shipment_date,status,
                 receiver_name,receiver_mobile,logistics_company,tracking_number,
                 shipping_address,signed_at_utc,remark,version,created_at_utc,
                 created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@CustomerId,@ProjectId,@ShipmentDate,'Preparing',
                 @ReceiverName,@ReceiverMobile,@LogisticsCompany,@TrackingNumber,
                 @ShippingAddress,NULL,@Remark,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                Code = code,
                data.CustomerId,
                data.ProjectId,
                data.ShipmentDate,
                data.ReceiverName,
                data.ReceiverMobile,
                data.LogisticsCompany,
                data.TrackingNumber,
                data.ShippingAddress,
                data.Remark,
                data.NowUtc,
                data.ActorUserId,
            }, transaction, cancellationToken));
        foreach (var item in data.Items)
        {
            await InsertItemAsync(connection, transaction, id, item,
                data.ActorUserId, data.NowUtc, cancellationToken);
        }
        await AuditAsync(connection, transaction, data.ActorUserId,
            "shipment.created", id, code, $"创建出货单 {code}", null,
            Snapshot(data, "Preparing"), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<ShipmentDetails?> UpdateAsync(
        ulong shipmentId,
        ShipmentWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var before = await LockAsync(connection, transaction, shipmentId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (before.ArchivedAtUtc is not null)
            throw FlowHearthValidationException.For("shipmentId", "归档出货单不能修改。");
        var status = Enum.Parse<ShipmentStatus>(before.Status);
        var existing = (await connection.QueryAsync<ItemLockRow>(Command(
            """
            SELECT id AS Id,equipment_id AS EquipmentId,item_name AS ItemName,
                   manufacturer AS Manufacturer,model AS Model,quantity AS Quantity,
                   unit AS Unit,remark AS Remark,version AS Version
            FROM shipment_items
            WHERE shipment_id=@ShipmentId AND deleted_at_utc IS NULL
            ORDER BY id FOR UPDATE;
            """,
            new { ShipmentId = shipmentId }, transaction, cancellationToken))).ToArray();

        if (status is ShipmentStatus.Shipped or ShipmentStatus.InTransit)
        {
            if (before.CustomerId != data.CustomerId
                || before.ProjectId != data.ProjectId
                || before.ShipmentDate != data.ShipmentDate
                || !ItemsEqual(existing, data.Items))
                throw FlowHearthValidationException.For("items", "正式出货后不能修改客户、项目、出货日期或出货明细。");
        }
        else if (status == ShipmentStatus.Received)
        {
            if (before.CustomerId != data.CustomerId
                || before.ProjectId != data.ProjectId
                || before.ShipmentDate != data.ShipmentDate
                || before.ReceiverName != data.ReceiverName
                || before.ReceiverMobile != data.ReceiverMobile
                || before.LogisticsCompany != data.LogisticsCompany
                || before.TrackingNumber != data.TrackingNumber
                || before.ShippingAddress != data.ShippingAddress
                || !ItemsEqual(existing, data.Items))
                throw FlowHearthValidationException.For("shipmentId", "已签收出货单只允许修正备注。");
        }
        else if (status == ShipmentStatus.Cancelled)
        {
            throw FlowHearthValidationException.For("shipmentId", "已取消出货单不能修改。");
        }

        if (status == ShipmentStatus.Preparing)
        {
            await ValidateOwnersAndEquipmentAsync(connection, transaction, data, cancellationToken);
            await ReplaceItemsAsync(connection, transaction, shipmentId, existing, data,
                cancellationToken);
        }

        var affected = await connection.ExecuteAsync(Command(
            """
            UPDATE shipments
            SET customer_id=@CustomerId,project_id=@ProjectId,shipment_date=@ShipmentDate,
                receiver_name=@ReceiverName,receiver_mobile=@ReceiverMobile,
                logistics_company=@LogisticsCompany,tracking_number=@TrackingNumber,
                shipping_address=@ShippingAddress,remark=@Remark,version=version+1,
                updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ShipmentId AND version=@Version;
            """,
            new
            {
                ShipmentId = shipmentId,
                data.CustomerId,
                data.ProjectId,
                data.ShipmentDate,
                data.ReceiverName,
                data.ReceiverMobile,
                data.LogisticsCompany,
                data.TrackingNumber,
                data.ShippingAddress,
                data.Remark,
                data.NowUtc,
                data.ActorUserId,
                data.Version,
            }, transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        await AuditAsync(connection, transaction, data.ActorUserId,
            "shipment.updated", shipmentId, before.Code, $"修改出货单 {before.Code}",
            before with { Items = existing }, Snapshot(data, status.ToString()),
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(shipmentId, cancellationToken);
    }

    public async Task<ShipmentDetails?> TransitionAsync(
        ulong shipmentId,
        ulong version,
        ShipmentStatus target,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var before = await LockAsync(connection, transaction, shipmentId, cancellationToken);
            if (before is null || before.Version != version)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            if (before.ArchivedAtUtc is not null)
                throw FlowHearthValidationException.For("shipmentId", "归档出货单不能推进状态。");
            var current = Enum.Parse<ShipmentStatus>(before.Status);
            if (!ShipmentStatusPolicy.CanTransition(current, target))
                throw FlowHearthValidationException.For("status", $"不能从 {current} 转换到 {target}。");
            if (target == ShipmentStatus.Shipped)
            {
                await ValidateFormalOwnersAsync(connection, transaction,
                    before.CustomerId, before.ProjectId, cancellationToken);
                var equipmentIds = (await connection.QueryAsync<ulong>(Command(
                    """
                    SELECT equipment_id
                    FROM shipment_items
                    WHERE shipment_id=@ShipmentId AND deleted_at_utc IS NULL
                      AND equipment_id IS NOT NULL
                    ORDER BY equipment_id;
                    """, new { ShipmentId = shipmentId }, transaction, cancellationToken))).ToArray();
                var itemCount = await connection.ExecuteScalarAsync<int>(Command(
                    "SELECT COUNT(*) FROM shipment_items WHERE shipment_id=@ShipmentId AND deleted_at_utc IS NULL;",
                    new { ShipmentId = shipmentId }, transaction, cancellationToken));
                if (itemCount == 0)
                    throw FlowHearthValidationException.For("items", "确认出货前至少需要一条出货明细。");
                await LockEquipmentAsync(connection, transaction, equipmentIds,
                    before.CustomerId, before.ProjectId, cancellationToken);
                await RejectFormalDuplicatesAsync(connection, transaction, shipmentId,
                    equipmentIds, cancellationToken);
            }
            var affected = await connection.ExecuteAsync(Command(
                """
                UPDATE shipments
                SET status=@Status,version=version+1,updated_at_utc=@NowUtc,
                    updated_by_user_id=@ActorUserId
                WHERE id=@ShipmentId AND version=@Version;
                """,
                new
                {
                    Status = target.ToString(),
                    ShipmentId = shipmentId,
                    Version = version,
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                }, transaction, cancellationToken));
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
            var action = target switch
            {
                ShipmentStatus.Shipped => "shipment.shipped",
                ShipmentStatus.InTransit => "shipment.in_transit",
                ShipmentStatus.Cancelled => "shipment.cancelled",
                _ => throw new ArgumentOutOfRangeException(nameof(target)),
            };
            await AuditAsync(connection, transaction, actorUserId, action,
                shipmentId, before.Code, $"出货单 {before.Code} 状态变更为 {target}",
                new { status = current, version },
                new { status = target, version = version + 1 }, nowUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(shipmentId, cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number is 1205 or 1213)
        {
            throw new ConflictException("设备交付状态正被其他操作修改，请刷新后重试。");
        }
    }

    public async Task<ShipmentDetails?> ReceiveAsync(
        ulong shipmentId,
        ShipmentReceiveData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var before = await LockAsync(connection, transaction, shipmentId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if (before.ArchivedAtUtc is not null)
            throw FlowHearthValidationException.For("shipmentId", "归档出货单不能签收。");
        var current = Enum.Parse<ShipmentStatus>(before.Status);
        if (!ShipmentStatusPolicy.CanTransition(current, ShipmentStatus.Received))
            throw FlowHearthValidationException.For("status", "只有已出货或运输中的出货单可以签收。");
        var receiverName = data.ReceiverName ?? before.ReceiverName;
        var remark = data.Remark ?? before.Remark;
        var affected = await connection.ExecuteAsync(Command(
            """
            UPDATE shipments
            SET status='Received',signed_at_utc=@SignedAtUtc,receiver_name=@ReceiverName,
                remark=@Remark,version=version+1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@ShipmentId AND version=@Version;
            """,
            new
            {
                ShipmentId = shipmentId,
                data.SignedAtUtc,
                ReceiverName = receiverName,
                Remark = remark,
                data.NowUtc,
                data.ActorUserId,
                data.Version,
            }, transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        await AuditAsync(connection, transaction, data.ActorUserId,
            "shipment.received", shipmentId, before.Code, $"确认签收出货单 {before.Code}",
            new { status = current, before.SignedAtUtc, before.ReceiverName, before.Remark, data.Version },
            new { status = ShipmentStatus.Received, data.SignedAtUtc, ReceiverName = receiverName, Remark = remark, version = data.Version + 1 },
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(shipmentId, cancellationToken);
    }

    public async Task<ShipmentDetails?> SetArchivedAsync(
        ulong shipmentId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LockAsync(connection, transaction, shipmentId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        if ((before.ArchivedAtUtc is not null) == data.Archived)
            throw FlowHearthValidationException.For("archived", data.Archived ? "出货单已经归档。" : "出货单尚未归档。");
        var status = Enum.Parse<ShipmentStatus>(before.Status);
        if (data.Archived && status is not (ShipmentStatus.Preparing or ShipmentStatus.Cancelled))
            throw FlowHearthValidationException.For("archived", "正式出货后的交付事实不能归档。");
        var affected = await connection.ExecuteAsync(Command(
            """
            UPDATE shipments
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@ShipmentId AND version=@Version;
            """,
            new
            {
                ShipmentId = shipmentId,
                Archived = data.Archived ? 1 : 0,
                data.NowUtc,
                data.ActorUserId,
                data.Version,
            }, transaction, cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        await AuditAsync(connection, transaction, data.ActorUserId,
            data.Archived ? "shipment.archived" : "shipment.restored",
            shipmentId, before.Code,
            $"{(data.Archived ? "归档" : "恢复")}出货单 {before.Code}",
            new { archived = !data.Archived, data.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(shipmentId, cancellationToken);
    }

    public async Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT e.id AS Id,e.equipment_code AS Code,e.name AS Name,
                   e.manufacturer AS Manufacturer,e.model AS Model,
                   (formal.shipment_id IS NOT NULL) AS IsDelivered,
                   formal.shipment_code AS ShipmentCode
            FROM equipment e
            LEFT JOIN
            (
                SELECT si.equipment_id,sh.id AS shipment_id,sh.shipment_code
                FROM shipment_items si
                INNER JOIN shipments sh ON sh.id=si.shipment_id
                WHERE si.deleted_at_utc IS NULL AND sh.archived_at_utc IS NULL
                  AND sh.status IN ('Shipped','InTransit','Received')
            ) formal ON formal.equipment_id=e.id
            WHERE e.project_id=@ProjectId AND e.archived_at_utc IS NULL
            ORDER BY IsDelivered,e.equipment_code,e.id
            LIMIT 200;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<EquipmentCandidateRow>(
            Command(sql, new { ProjectId = projectId }, null, cancellationToken));
        return rows.Select(row => row.ToCandidate()).ToArray();
    }

    public async Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(
        ulong equipmentId,
        CancellationToken cancellationToken)
    {
        var sql = SummarySelect +
            """
             INNER JOIN shipment_items si_lookup ON si_lookup.shipment_id=sh.id
                AND si_lookup.deleted_at_utc IS NULL
             WHERE si_lookup.equipment_id=@EquipmentId
               AND sh.archived_at_utc IS NULL
               AND sh.status IN ('Shipped','InTransit','Received')
             ORDER BY sh.shipment_date DESC,sh.id DESC LIMIT 1;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ShipmentRow>(
            Command(sql, new { EquipmentId = equipmentId }, null, cancellationToken));
        return new(equipmentId, row is not null, row?.ToSummary());
    }

    public async Task<ProjectShipmentMetrics> GetProjectMetricsAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT @ProjectId AS ProjectId,
                   (SELECT COUNT(*) FROM shipments sh
                    WHERE sh.project_id=@ProjectId AND sh.archived_at_utc IS NULL
                      AND sh.status IN ('Shipped','InTransit','Received')) AS ShipmentCount,
                   (SELECT COUNT(*) FROM shipments sh
                    WHERE sh.project_id=@ProjectId AND sh.archived_at_utc IS NULL
                      AND sh.status='Received') AS ReceivedShipmentCount,
                   (SELECT COUNT(DISTINCT si.equipment_id)
                    FROM shipment_items si
                    INNER JOIN shipments sh ON sh.id=si.shipment_id
                    WHERE sh.project_id=@ProjectId AND sh.archived_at_utc IS NULL
                      AND sh.status IN ('Shipped','InTransit','Received')
                      AND si.deleted_at_utc IS NULL AND si.equipment_id IS NOT NULL)
                      AS EquipmentDeliveryCount,
                   (SELECT COUNT(*) FROM equipment e
                    WHERE e.project_id=@ProjectId AND e.archived_at_utc IS NULL)
                      AS TotalEquipmentCount,
                   (SELECT MAX(sh.shipment_date) FROM shipments sh
                    WHERE sh.project_id=@ProjectId AND sh.archived_at_utc IS NULL
                      AND sh.status IN ('Shipped','InTransit','Received')) AS LastShipmentDate;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<ProjectMetricsRow>(
            Command(sql, new { ProjectId = projectId }, null, cancellationToken));
        return new(row.ProjectId, row.ShipmentCount, row.ReceivedShipmentCount,
            row.EquipmentDeliveryCount, row.TotalEquipmentCount,
            row.TotalEquipmentCount > 0
                && row.EquipmentDeliveryCount >= row.TotalEquipmentCount,
            row.LastShipmentDate);
    }

    private static async Task<ShipmentDetails?> GetAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ulong shipmentId,
        CancellationToken cancellationToken)
    {
        var sql = SummarySelect +
            """
             WHERE sh.id=@ShipmentId;
             SELECT si.id AS Id,si.equipment_id AS EquipmentId,
                    e.equipment_code AS EquipmentCode,si.item_name AS ItemName,
                    si.manufacturer AS Manufacturer,si.model AS Model,
                    si.quantity AS Quantity,si.unit AS Unit,si.remark AS Remark,
                    si.version AS Version
             FROM shipment_items si
             LEFT JOIN equipment e ON e.id=si.equipment_id
             WHERE si.shipment_id=@ShipmentId AND si.deleted_at_utc IS NULL
             ORDER BY si.id;
            """;
        using var result = await connection.QueryMultipleAsync(Command(
            sql, new { ShipmentId = shipmentId }, transaction, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<ShipmentDetailsRow>();
        if (header is null) return null;
        var items = (await result.ReadAsync<ShipmentItemDetails>()).ToArray();
        return header.ToDetails(items);
    }

    private static async Task ValidateOwnersAndEquipmentAsync(
        DbConnection connection,
        DbTransaction transaction,
        ShipmentWriteData data,
        CancellationToken cancellationToken)
    {
        var project = await connection.QuerySingleOrDefaultAsync<ProjectOwnerRow>(Command(
            """
            SELECT p.id AS Id,p.customer_id AS CustomerId,p.archived_at_utc AS ArchivedAtUtc,
                   c.archived_at_utc AS CustomerArchivedAtUtc
            FROM projects p
            INNER JOIN customers c ON c.id=p.customer_id
            WHERE p.id=@ProjectId FOR SHARE;
            """,
            new { data.ProjectId }, transaction, cancellationToken));
        if (project is null || project.ArchivedAtUtc is not null
            || project.CustomerArchivedAtUtc is not null)
            throw FlowHearthValidationException.For("projectId", "项目或所属客户不存在或已归档。");
        if (project.CustomerId != data.CustomerId)
            throw FlowHearthValidationException.For("customerId", "出货客户必须与项目所属客户一致。");
        var equipmentIds = data.Items.Where(item => item.EquipmentId.HasValue)
            .Select(item => item.EquipmentId!.Value).OrderBy(id => id).ToArray();
        if (equipmentIds.Length == 0) return;
        var equipment = (await connection.QueryAsync<EquipmentOwnerRow>(Command(
            """
            SELECT id AS Id,project_id AS ProjectId,customer_id AS CustomerId,
                   archived_at_utc AS ArchivedAtUtc
            FROM equipment
            WHERE id IN @EquipmentIds
            ORDER BY id FOR SHARE;
            """,
            new { EquipmentIds = equipmentIds }, transaction, cancellationToken))).ToArray();
        if (equipment.Length != equipmentIds.Length
            || equipment.Any(row => row.ArchivedAtUtc is not null
                || row.ProjectId != data.ProjectId
                || row.CustomerId != data.CustomerId))
            throw FlowHearthValidationException.For("items", "设备必须有效且属于当前客户和项目。");
    }

    private static async Task ReplaceItemsAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong shipmentId,
        IReadOnlyList<ItemLockRow> existing,
        ShipmentWriteData data,
        CancellationToken cancellationToken)
    {
        var existingById = existing.ToDictionary(row => row.Id);
        var suppliedIds = data.Items.Where(item => item.Id.HasValue)
            .Select(item => item.Id!.Value).ToHashSet();
        if (suppliedIds.Any(id => !existingById.ContainsKey(id)))
            throw FlowHearthValidationException.For("items", "出货明细不属于当前出货单。");
        foreach (var row in existing.Where(row => !suppliedIds.Contains(row.Id)))
        {
            await connection.ExecuteAsync(Command(
                """
                UPDATE shipment_items
                SET deleted_at_utc=@NowUtc,deleted_by_user_id=@ActorUserId,
                    version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
                WHERE id=@ItemId;
                """,
                new { data.NowUtc, data.ActorUserId, ItemId = row.Id },
                transaction, cancellationToken));
        }
        foreach (var item in data.Items)
        {
            if (!item.Id.HasValue)
            {
                await InsertItemAsync(connection, transaction, shipmentId, item,
                    data.ActorUserId, data.NowUtc, cancellationToken);
                continue;
            }
            await connection.ExecuteAsync(Command(
                """
                UPDATE shipment_items
                SET equipment_id=@EquipmentId,item_name=@ItemName,
                    manufacturer=@Manufacturer,model=@Model,quantity=@Quantity,
                    unit=@Unit,remark=@Remark,version=version+1,
                    updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
                WHERE id=@Id;
                """,
                new
                {
                    item.Id,
                    item.EquipmentId,
                    item.ItemName,
                    item.Manufacturer,
                    item.Model,
                    item.Quantity,
                    item.Unit,
                    item.Remark,
                    data.NowUtc,
                    data.ActorUserId,
                }, transaction, cancellationToken));
        }
    }

    private static Task<int> InsertItemAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong shipmentId,
        ShipmentItemCommand item,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Command(
            """
            INSERT INTO shipment_items
                (shipment_id,equipment_id,item_name,manufacturer,model,quantity,
                 unit,remark,version,created_at_utc,created_by_user_id,
                 updated_at_utc,updated_by_user_id)
            VALUES
                (@ShipmentId,@EquipmentId,@ItemName,@Manufacturer,@Model,@Quantity,
                 @Unit,@Remark,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            """,
            new
            {
                ShipmentId = shipmentId,
                item.EquipmentId,
                item.ItemName,
                item.Manufacturer,
                item.Model,
                item.Quantity,
                item.Unit,
                item.Remark,
                NowUtc = nowUtc,
                ActorUserId = actorUserId,
            }, transaction, cancellationToken));

    private static async Task LockEquipmentAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong[] equipmentIds,
        ulong customerId,
        ulong projectId,
        CancellationToken cancellationToken)
    {
        if (equipmentIds.Length == 0) return;
        var locked = (await connection.QueryAsync<EquipmentOwnerRow>(Command(
            """
            SELECT id AS Id,project_id AS ProjectId,customer_id AS CustomerId,
                   archived_at_utc AS ArchivedAtUtc
            FROM equipment
            WHERE id IN @EquipmentIds
            ORDER BY id FOR UPDATE;
            """,
            new { EquipmentIds = equipmentIds }, transaction, cancellationToken))).ToArray();
        if (locked.Length != equipmentIds.Length
            || locked.Any(row => row.ArchivedAtUtc is not null
                || row.ProjectId != projectId || row.CustomerId != customerId))
            throw FlowHearthValidationException.For(
                "items", "确认出货时设备必须有效且仍属于当前客户和项目。");
    }

    private static async Task ValidateFormalOwnersAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong customerId,
        ulong projectId,
        CancellationToken cancellationToken)
    {
        var valid = await connection.QuerySingleOrDefaultAsync<ulong?>(Command(
            """
            SELECT p.id
            FROM projects p
            INNER JOIN customers c ON c.id=p.customer_id
            WHERE p.id=@ProjectId AND p.customer_id=@CustomerId
              AND p.archived_at_utc IS NULL AND c.archived_at_utc IS NULL
            FOR SHARE;
            """,
            new { ProjectId = projectId, CustomerId = customerId },
            transaction, cancellationToken));
        if (!valid.HasValue)
            throw FlowHearthValidationException.For(
                "projectId", "确认出货时项目及所属客户必须有效且未归档。");
    }

    private static async Task RejectFormalDuplicatesAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong shipmentId,
        ulong[] equipmentIds,
        CancellationToken cancellationToken)
    {
        if (equipmentIds.Length == 0) return;
        var duplicate = await connection.QuerySingleOrDefaultAsync<DuplicateEquipmentRow>(Command(
            """
            SELECT si.equipment_id AS EquipmentId,sh.shipment_code AS ShipmentCode
            FROM shipment_items si
            INNER JOIN shipments sh ON sh.id=si.shipment_id
            WHERE si.equipment_id IN @EquipmentIds
              AND si.deleted_at_utc IS NULL
              AND sh.id<>@ShipmentId
              AND sh.archived_at_utc IS NULL
              AND sh.status IN ('Shipped','InTransit','Received')
            ORDER BY si.equipment_id,sh.id LIMIT 1;
            """,
            new { EquipmentIds = equipmentIds, ShipmentId = shipmentId },
            transaction, cancellationToken));
        if (duplicate is not null)
            throw new ConflictException(
                $"设备已通过出货单 {duplicate.ShipmentCode} 正式交付，不能重复出货。");
    }

    private static bool ItemsEqual(
        IReadOnlyList<ItemLockRow> current,
        IReadOnlyList<ShipmentItemCommand> supplied)
    {
        if (current.Count != supplied.Count) return false;
        var suppliedById = supplied.Where(item => item.Id.HasValue)
            .ToDictionary(item => item.Id!.Value);
        return suppliedById.Count == current.Count && current.All(row =>
            suppliedById.TryGetValue(row.Id, out var item)
            && row.EquipmentId == item.EquipmentId
            && row.ItemName == item.ItemName
            && row.Manufacturer == item.Manufacturer
            && row.Model == item.Model
            && row.Quantity == item.Quantity
            && row.Unit == item.Unit
            && row.Remark == item.Remark);
    }

    private static Task<ShipmentLockRow?> LockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong shipmentId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<ShipmentLockRow>(Command(
            """
            SELECT id AS Id,shipment_code AS Code,customer_id AS CustomerId,
                   project_id AS ProjectId,shipment_date AS ShipmentDate,status AS Status,
                   receiver_name AS ReceiverName,receiver_mobile AS ReceiverMobile,
                   logistics_company AS LogisticsCompany,tracking_number AS TrackingNumber,
                   shipping_address AS ShippingAddress,signed_at_utc AS SignedAtUtc,
                   remark AS Remark,version AS Version,archived_at_utc AS ArchivedAtUtc
            FROM shipments WHERE id=@ShipmentId FOR UPDATE;
            """,
            new { ShipmentId = shipmentId }, transaction, cancellationToken));

    private static Task<ulong> NextSequenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sequenceName,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<ulong>(Command(
            """
            INSERT INTO number_sequences
                (sequence_name,current_value,version,updated_at_utc)
            VALUES (@SequenceName,LAST_INSERT_ID(1),1,@NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value=LAST_INSERT_ID(current_value+1),version=version+1,
                updated_at_utc=VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """,
            new { SequenceName = sequenceName, NowUtc = nowUtc },
            transaction, cancellationToken));

    private static Task<int> AuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        ulong entityId,
        string code,
        string summary,
        object? before,
        object? after,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(Command(
            """
            INSERT INTO audit_logs
                (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                 entity_code,summary,before_json,after_json)
            VALUES
                (@NowUtc,@ActorUserId,@Action,'shipment',@EntityId,@Code,
                 @Summary,@BeforeJson,@AfterJson);
            """,
            new
            {
                NowUtc = nowUtc,
                ActorUserId = actorUserId,
                Action = action,
                EntityId = entityId,
                Code = code,
                Summary = summary,
                BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
                AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            }, transaction, cancellationToken));

    private static object Snapshot(ShipmentWriteData data, string status) => new
    {
        data.CustomerId,
        data.ProjectId,
        data.ShipmentDate,
        status,
        data.ReceiverName,
        data.ReceiverMobile,
        data.LogisticsCompany,
        data.TrackingNumber,
        data.ShippingAddress,
        data.Remark,
        items = data.Items.Select(item => new
        {
            item.Id,
            item.EquipmentId,
            item.ItemName,
            item.Manufacturer,
            item.Model,
            item.Quantity,
            item.Unit,
            item.Remark,
        }),
    };

    private static string OrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"sh.shipment_code {direction},sh.id {direction}",
            "shipmentdate" => $"sh.shipment_date {direction},sh.id {direction}",
            "status" => $"sh.status {direction},sh.id {direction}",
            "signedat" => $"sh.signed_at_utc {direction},sh.id {direction}",
            "createdat" => $"sh.created_at_utc {direction},sh.id {direction}",
            _ => $"sh.updated_at_utc {direction},sh.id {direction}",
        };
    }

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private class ShipmentRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public DateOnly ShipmentDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ReceiverName { get; init; }
        public string? LogisticsCompany { get; init; }
        public string? TrackingNumber { get; init; }
        public DateTime? SignedAtUtc { get; init; }
        public int ItemCount { get; init; }
        public int EquipmentCount { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ShipmentSummary ToSummary() => new(
            Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId,
            ProjectCode, ProjectName, ShipmentDate, Enum.Parse<ShipmentStatus>(Status),
            ReceiverName, LogisticsCompany, TrackingNumber, SignedAtUtc, ItemCount,
            EquipmentCount, IsArchived, Version, UpdatedAtUtc);
    }

    private sealed class ShipmentDetailsRow : ShipmentRow
    {
        public string? ReceiverMobile { get; init; }
        public string ShippingAddress { get; init; } = string.Empty;
        public string? Remark { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public DateTime CreatedAtUtc { get; init; }

        public ShipmentDetails ToDetails(IReadOnlyList<ShipmentItemDetails> items) => new(
            Id, Code, CustomerId, CustomerCode, CustomerName, ProjectId, ProjectCode,
            ProjectName, ShipmentDate, Enum.Parse<ShipmentStatus>(Status), ReceiverName,
            ReceiverMobile, LogisticsCompany, TrackingNumber, ShippingAddress, SignedAtUtc,
            Remark, ItemCount, EquipmentCount, IsArchived, ArchivedAtUtc, Version,
            CreatedAtUtc, UpdatedAtUtc, items);
    }

    private sealed record ShipmentLockRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public ulong ProjectId { get; init; }
        public DateOnly ShipmentDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ReceiverName { get; init; }
        public string? ReceiverMobile { get; init; }
        public string? LogisticsCompany { get; init; }
        public string? TrackingNumber { get; init; }
        public string ShippingAddress { get; init; } = string.Empty;
        public DateTime? SignedAtUtc { get; init; }
        public string? Remark { get; init; }
        public ulong Version { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public IReadOnlyList<ItemLockRow> Items { get; init; } = [];
    }

    private sealed class ItemLockRow
    {
        public ulong Id { get; init; }
        public ulong? EquipmentId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public decimal Quantity { get; init; }
        public string Unit { get; init; } = string.Empty;
        public string? Remark { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class ProjectOwnerRow
    {
        public ulong Id { get; init; }
        public ulong CustomerId { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public DateTime? CustomerArchivedAtUtc { get; init; }
    }

    private sealed class EquipmentOwnerRow
    {
        public ulong Id { get; init; }
        public ulong CustomerId { get; init; }
        public ulong? ProjectId { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
    }

    private sealed class EquipmentCandidateRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public bool IsDelivered { get; init; }
        public string? ShipmentCode { get; init; }

        public ShipmentEquipmentCandidate ToCandidate() => new(
            Id, Code, Name, Manufacturer, Model, IsDelivered, ShipmentCode);
    }

    private sealed class DuplicateEquipmentRow
    {
        public ulong EquipmentId { get; init; }
        public string ShipmentCode { get; init; } = string.Empty;
    }

    private sealed class ProjectMetricsRow
    {
        public ulong ProjectId { get; init; }
        public int ShipmentCount { get; init; }
        public int ReceivedShipmentCount { get; init; }
        public int EquipmentDeliveryCount { get; init; }
        public int TotalEquipmentCount { get; init; }
        public DateOnly? LastShipmentDate { get; init; }
    }
}
