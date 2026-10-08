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

namespace FlowHearth.Infrastructure.Finance;

public sealed class MySqlPurchaseOrderRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IPurchaseOrderRepository
{
    private const string SummarySelect =
        """
        SELECT po.id AS Id,po.purchase_order_code AS Code,
               po.supplier_id AS SupplierId,s.supplier_code AS SupplierCode,s.name AS SupplierName,
               po.project_id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
               po.order_date AS OrderDate,po.status AS Status,po.total_amount AS TotalAmount,
               po.expected_delivery_date AS ExpectedDeliveryDate,
               po.contact_name AS ContactName,po.delivery_address AS DeliveryAddress,
               po.remark AS Remark,po.archived_at_utc AS ArchivedAtUtc,
               po.created_at_utc AS CreatedAtUtc,
               COALESCE(q.OrderedQuantity,0) AS OrderedQuantity,
               COALESCE(q.ReceivedQuantity,0) AS ReceivedQuantity,
               CASE WHEN COALESCE(q.OrderedQuantity,0)=0 THEN 0
                    ELSE ROUND(q.ReceivedQuantity/q.OrderedQuantity*100,2) END AS ReceiptProgress,
               COALESCE(q.ItemCount,0) AS ItemCount,
               COALESCE(q.ReceivedItemCount,0) AS ReceivedItemCount,
               COALESCE(q.PartiallyReceivedItemCount,0) AS PartiallyReceivedItemCount,
               COALESCE(q.NotReceivedItemCount,0) AS NotReceivedItemCount,
               u.display_name AS CreatedByDisplayName,
               (po.archived_at_utc IS NOT NULL) AS IsArchived,
               po.version AS Version,po.updated_at_utc AS UpdatedAtUtc
        FROM purchase_orders po
        INNER JOIN suppliers s ON s.id=po.supplier_id
        INNER JOIN projects p ON p.id=po.project_id
        LEFT JOIN users u ON u.id=po.created_by_user_id
        LEFT JOIN
        (
            SELECT poi.purchase_order_id,SUM(poi.quantity) AS OrderedQuantity,
                   SUM(COALESCE(r.ReceivedQuantity,0)) AS ReceivedQuantity,
                   COUNT(*) AS ItemCount,
                   SUM(CASE WHEN COALESCE(r.ReceivedQuantity,0)=poi.quantity THEN 1 ELSE 0 END) AS ReceivedItemCount,
                   SUM(CASE WHEN COALESCE(r.ReceivedQuantity,0)>0 AND COALESCE(r.ReceivedQuantity,0)<poi.quantity THEN 1 ELSE 0 END) AS PartiallyReceivedItemCount,
                   SUM(CASE WHEN COALESCE(r.ReceivedQuantity,0)=0 THEN 1 ELSE 0 END) AS NotReceivedItemCount
            FROM purchase_order_items poi
            LEFT JOIN (SELECT purchase_order_item_id,SUM(quantity_received) AS ReceivedQuantity
                       FROM purchase_receipt_items GROUP BY purchase_order_item_id) r
                ON r.purchase_order_item_id=poi.id
            WHERE poi.deleted_at_utc IS NULL GROUP BY poi.purchase_order_id
        ) q ON q.purchase_order_id=po.id
        """;

    public async Task<PagedResult<PurchaseOrderSummary>> ListAsync(PurchaseOrderListCriteria criteria, CancellationToken cancellationToken)
    {
        var orderBy = GetOrderBy(criteria.SortBy, criteria.SortDescending);
        var where =
            """
            WHERE (@ArchiveMode=2 OR (@ArchiveMode=0 AND po.archived_at_utc IS NULL) OR (@ArchiveMode=1 AND po.archived_at_utc IS NOT NULL))
              AND (@SupplierId IS NULL OR po.supplier_id=@SupplierId)
              AND (@ProjectId IS NULL OR po.project_id=@ProjectId)
              AND (@Status IS NULL OR po.status=@Status)
              AND (@OrderFrom IS NULL OR po.order_date>=@OrderFrom)
              AND (@OrderTo IS NULL OR po.order_date<=@OrderTo)
              AND (@SearchPattern IS NULL OR CONVERT(po.purchase_order_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=' OR s.name LIKE @SearchPattern ESCAPE '=' OR p.name LIKE @SearchPattern ESCAPE '=')
            """;
        var sql = $"{SummarySelect} {where} ORDER BY {orderBy} LIMIT @PageSize OFFSET @Offset; SELECT COUNT(*) FROM purchase_orders po INNER JOIN suppliers s ON s.id=po.supplier_id INNER JOIN projects p ON p.id=po.project_id {where};";
        var parameters = new
        {
            ArchiveMode = (int)criteria.ArchiveMode,
            criteria.SupplierId,
            criteria.ProjectId,
            Status = criteria.Status?.ToString(),
            criteria.OrderFrom,
            criteria.OrderTo,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<SummaryRow>()).Select(MapSummary).ToArray();
        return new(items, criteria.Page, criteria.PageSize, await result.ReadSingleAsync<long>());
    }

    public async Task<PurchaseOrderDetails?> GetAsync(ulong purchaseOrderId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, null, purchaseOrderId, cancellationToken);
    }

    public async Task<PurchaseOrderDetails> CreateAsync(PurchaseOrderWriteData data, CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await ValidateOwnersAsync(connection, tx, data.SupplierId, data.ProjectId, cancellationToken);
        var sequence = await NextSequenceAsync(connection, tx, $"purchase_order:{data.NowUtc.Year:D4}", data.NowUtc, cancellationToken);
        var code = FinanceCodes.Format(runtime.NumberPrefixes.Purchase, data.NowUtc.Year, sequence);
        var total = data.Items.Sum(x => PurchaseOrderPolicy.CalculateItemAmount(x.Quantity, x.UnitPrice));
        var id = await connection.QuerySingleAsync<ulong>(Command(
            """
            INSERT INTO purchase_orders
                (purchase_order_code,supplier_id,project_id,order_date,status,total_amount,contact_name,delivery_address,expected_delivery_date,remark,version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES (@Code,@SupplierId,@ProjectId,@OrderDate,'Draft',@Total,@ContactName,@DeliveryAddress,@ExpectedDeliveryDate,@Remark,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """, new { Code = code, data.SupplierId, data.ProjectId, data.OrderDate, Total = total, data.ContactName, data.DeliveryAddress, data.ExpectedDeliveryDate, data.Remark, data.NowUtc, data.ActorUserId }, tx, cancellationToken));
        foreach (var item in data.Items) await InsertItemAsync(connection, tx, id, item, data.ActorUserId, data.NowUtc, cancellationToken);
        await AuditAsync(connection, tx, data.ActorUserId, "purchase_order.created", id, code, $"创建采购单 {code}", null, Snapshot(data, total, "Draft"), data.NowUtc, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<PurchaseOrderDetails?> UpdateAsync(ulong id, PurchaseOrderWriteData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var before = await LockOrderAsync(connection, tx, id, cancellationToken);
        if (before is null || before.Version != data.Version) { await tx.RollbackAsync(cancellationToken); return null; }
        if (before.ArchivedAtUtc is not null) throw FlowHearthValidationException.For("purchaseOrderId", "归档采购单不能修改。");
        var currentStatus = Enum.Parse<PurchaseOrderStatus>(before.Status);
        if (currentStatus is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled) throw FlowHearthValidationException.For("status", "已收货或已取消的采购单不能修改。");
        await ValidateOwnersAsync(connection, tx, data.SupplierId, data.ProjectId, cancellationToken);
        var existing = (await connection.QueryAsync<ItemLockRow>(Command(
            """SELECT poi.id AS Id,poi.item_name AS ItemName,poi.manufacturer AS Manufacturer,poi.model AS Model,poi.specification AS Specification,poi.quantity AS Quantity,poi.unit AS Unit,poi.unit_price AS UnitPrice,COALESCE(SUM(pri.quantity_received),0) AS ReceivedQuantity FROM purchase_order_items poi LEFT JOIN purchase_receipt_items pri ON pri.purchase_order_item_id=poi.id WHERE poi.purchase_order_id=@Id AND poi.deleted_at_utc IS NULL GROUP BY poi.id FOR UPDATE;""", new { Id = id }, tx, cancellationToken))).ToDictionary(x => x.Id);
        if (existing.Values.Any(x => x.ReceivedQuantity > 0)
            && (before.SupplierId != data.SupplierId || before.ProjectId != data.ProjectId))
        {
            throw FlowHearthValidationException.For(
                "supplierId",
                "已有收货记录时不能更换供应商或关联项目。");
        }
        var suppliedIds = data.Items.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
        if (suppliedIds.Any(x => !existing.ContainsKey(x))) throw FlowHearthValidationException.For("items", "采购明细不属于当前采购单。");
        foreach (var old in existing.Values.Where(x => !suppliedIds.Contains(x.Id)))
        {
            if (old.ReceivedQuantity > 0) throw FlowHearthValidationException.For("items", "已有收货记录的采购明细不能删除。");
            await connection.ExecuteAsync(Command("UPDATE purchase_order_items SET deleted_at_utc=@NowUtc,deleted_by_user_id=@Actor,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@Actor WHERE id=@ItemId;", new { data.NowUtc, Actor = data.ActorUserId, ItemId = old.Id }, tx, cancellationToken));
        }
        foreach (var item in data.Items)
        {
            if (item.Id is null) await InsertItemAsync(connection, tx, id, item, data.ActorUserId, data.NowUtc, cancellationToken);
            else
            {
                if (item.Quantity < existing[item.Id.Value].ReceivedQuantity) throw FlowHearthValidationException.For("items", "采购数量不能小于累计收货数量。");
                var old = existing[item.Id.Value];
                if (old.ReceivedQuantity > 0
                    && (old.ItemName != item.ItemName
                        || old.Manufacturer != item.Manufacturer
                        || old.Model != item.Model
                        || old.Specification != item.Specification
                        || old.Unit != item.Unit
                        || old.UnitPrice != item.UnitPrice))
                {
                    throw FlowHearthValidationException.For(
                        "items",
                        "已有收货记录的采购行不能修改品名、制造商、型号、规格、单位或单价。");
                }
                await connection.ExecuteAsync(Command(
                    """UPDATE purchase_order_items SET item_name=@ItemName,manufacturer=@Manufacturer,model=@Model,specification=@Specification,quantity=@Quantity,unit=@Unit,unit_price=@UnitPrice,amount=@Amount,remark=@Remark,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@Actor WHERE id=@Id;""",
                    new { item.Id, item.ItemName, item.Manufacturer, item.Model, item.Specification, item.Quantity, item.Unit, item.UnitPrice, Amount = PurchaseOrderPolicy.CalculateItemAmount(item.Quantity, item.UnitPrice), item.Remark, data.NowUtc, Actor = data.ActorUserId }, tx, cancellationToken));
            }
        }
        var totals = await GetTotalsAsync(connection, tx, id, cancellationToken);
        var status = currentStatus == PurchaseOrderStatus.Draft ? currentStatus : PurchaseOrderPolicy.StatusFromReceiptTotals(totals.OrderedQuantity, totals.ReceivedQuantity);
        var affected = await connection.ExecuteAsync(Command(
            """UPDATE purchase_orders SET supplier_id=@SupplierId,project_id=@ProjectId,order_date=@OrderDate,status=@Status,total_amount=@Total,contact_name=@ContactName,delivery_address=@DeliveryAddress,expected_delivery_date=@ExpectedDeliveryDate,remark=@Remark,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId WHERE id=@Id AND version=@Version;""",
            new { Id = id, data.SupplierId, data.ProjectId, data.OrderDate, Status = status.ToString(), Total = totals.TotalAmount, data.ContactName, data.DeliveryAddress, data.ExpectedDeliveryDate, data.Remark, data.NowUtc, data.ActorUserId, data.Version }, tx, cancellationToken));
        if (affected != 1) { await tx.RollbackAsync(cancellationToken); return null; }
        await AuditAsync(connection, tx, data.ActorUserId, "purchase_order.updated", id, before.Code, $"修改采购单 {before.Code}", before, Snapshot(data, totals.TotalAmount, status.ToString()), data.NowUtc, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public Task<PurchaseOrderDetails?> OrderAsync(ulong id, ulong version, ulong actor, DateTime now, CancellationToken ct) => TransitionAsync(id, version, actor, now, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Ordered, "purchase_order.ordered", "下单", false, ct);
    public Task<PurchaseOrderDetails?> CancelAsync(ulong id, ulong version, ulong actor, DateTime now, CancellationToken ct) => TransitionAsync(id, version, actor, now, null, PurchaseOrderStatus.Cancelled, "purchase_order.cancelled", "取消", true, ct);

    public async Task<PurchaseOrderDetails?> SetArchivedAsync(ulong id, SetFinanceArchiveData data, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LockOrderAsync(connection, tx, id, cancellationToken);
        if (before is null || before.Version != data.Version) { await tx.RollbackAsync(cancellationToken); return null; }
        if ((before.ArchivedAtUtc is not null) == data.Archived) throw FlowHearthValidationException.For("archived", data.Archived ? "采购单已经归档。" : "采购单尚未归档。");
        var affected = await connection.ExecuteAsync(Command(
            """UPDATE purchase_orders SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,archived_by_user_id=CASE WHEN @Archived=1 THEN @Actor ELSE NULL END,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@Actor WHERE id=@Id AND version=@Version;""",
            new { Id = id, Archived = data.Archived ? 1 : 0, data.NowUtc, Actor = data.ActorUserId, data.Version }, tx, cancellationToken));
        if (affected != 1) { await tx.RollbackAsync(cancellationToken); return null; }
        await AuditAsync(connection, tx, data.ActorUserId, data.Archived ? "purchase_order.archived" : "purchase_order.restored", id, before.Code, $"{(data.Archived ? "归档" : "恢复")}采购单 {before.Code}", new { archived = !data.Archived, before.Version }, new { archived = data.Archived, version = data.Version + 1 }, data.NowUtc, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PurchaseOrderDetails?> ReceiveAsync(ulong id, PurchaseReceiptWriteData data, CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var before = await LockOrderAsync(connection, tx, id, cancellationToken);
        if (before is null || before.Version != data.PurchaseOrderVersion) { await tx.RollbackAsync(cancellationToken); return null; }
        if (before.ArchivedAtUtc is not null) throw FlowHearthValidationException.For("purchaseOrderId", "归档采购单不能收货。");
        var current = Enum.Parse<PurchaseOrderStatus>(before.Status);
        if (current is not (PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived)) throw FlowHearthValidationException.For("status", "只有已下单或部分收货的采购单可以收货。");
        var items = (await connection.QueryAsync<ItemLockRow>(Command(
            """SELECT poi.id AS Id,poi.quantity AS Quantity,COALESCE(SUM(pri.quantity_received),0) AS ReceivedQuantity FROM purchase_order_items poi LEFT JOIN purchase_receipt_items pri ON pri.purchase_order_item_id=poi.id WHERE poi.purchase_order_id=@Id AND poi.deleted_at_utc IS NULL GROUP BY poi.id,poi.quantity FOR UPDATE;""", new { Id = id }, tx, cancellationToken))).ToDictionary(x => x.Id);
        foreach (var item in data.Items)
        {
            if (!items.TryGetValue(item.PurchaseOrderItemId, out var existing)) throw FlowHearthValidationException.For("items", "收货明细不属于当前采购单。");
            if (item.QuantityReceived > existing.Quantity - existing.ReceivedQuantity) throw FlowHearthValidationException.For("items", "本次收货数量超过未收数量。");
        }
        var sequence = await NextSequenceAsync(connection, tx, $"purchase_receipt:{data.NowUtc.Year:D4}", data.NowUtc, cancellationToken);
        var code = FinanceCodes.Format(runtime.NumberPrefixes.PurchaseReceipt, data.NowUtc.Year, sequence);
        var receiptId = await connection.QuerySingleAsync<ulong>(Command(
            """INSERT INTO purchase_receipts (purchase_receipt_code,purchase_order_id,received_date,received_by_user_id,remark,created_at_utc,created_by_user_id) VALUES (@Code,@OrderId,@ReceivedDate,@Actor,@Remark,@NowUtc,@Actor); SELECT LAST_INSERT_ID();""",
            new { Code = code, OrderId = id, data.ReceivedDate, Actor = data.ActorUserId, data.Remark, data.NowUtc }, tx, cancellationToken));
        foreach (var item in data.Items) await connection.ExecuteAsync(Command("INSERT INTO purchase_receipt_items (purchase_receipt_id,purchase_order_item_id,quantity_received) VALUES (@ReceiptId,@ItemId,@Quantity);", new { ReceiptId = receiptId, ItemId = item.PurchaseOrderItemId, Quantity = item.QuantityReceived }, tx, cancellationToken));
        var totals = await GetTotalsAsync(connection, tx, id, cancellationToken);
        var status = PurchaseOrderPolicy.StatusFromReceiptTotals(totals.OrderedQuantity, totals.ReceivedQuantity);
        var affected = await connection.ExecuteAsync(Command("UPDATE purchase_orders SET status=@Status,version=version+1,updated_at_utc=@NowUtc,updated_by_user_id=@Actor WHERE id=@Id AND version=@Version;", new { Status = status.ToString(), data.NowUtc, Actor = data.ActorUserId, Id = id, Version = data.PurchaseOrderVersion }, tx, cancellationToken));
        if (affected != 1) { await tx.RollbackAsync(cancellationToken); return null; }
        await AuditAsync(connection, tx, data.ActorUserId, "purchase_receipt.created", receiptId, code, $"采购单 {before.Code} 收货", null, new { purchaseOrderId = id, data.ReceivedDate, items = data.Items }, data.NowUtc, cancellationToken, "purchase_receipt");
        await AuditAsync(connection, tx, data.ActorUserId, "purchase_order.received", id, before.Code, $"采购单 {before.Code} 收货并更新状态", new { status = before.Status, version = before.Version }, new { status = status.ToString(), version = before.Version + 1, totals.ReceivedQuantity }, data.NowUtc, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<PurchaseOrderDetails?> TransitionAsync(ulong id, ulong version, ulong actor, DateTime now, PurchaseOrderStatus? required, PurchaseOrderStatus target, string action, string verb, bool cancelling, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var before = await LockOrderAsync(connection, tx, id, ct);
        if (before is null || before.Version != version) { await tx.RollbackAsync(ct); return null; }
        if (before.ArchivedAtUtc is not null) throw FlowHearthValidationException.For("purchaseOrderId", "归档采购单不能执行此操作。");
        var current = Enum.Parse<PurchaseOrderStatus>(before.Status);
        if (required.HasValue && current != required) throw FlowHearthValidationException.For("status", "只有草稿采购单可以正式下单。");
        if (cancelling)
        {
            if (current is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered)) throw FlowHearthValidationException.For("status", "只有草稿或尚未收货的采购单可以取消。");
            var received = await connection.ExecuteScalarAsync<decimal>(Command("SELECT COALESCE(SUM(pri.quantity_received),0) FROM purchase_receipt_items pri INNER JOIN purchase_order_items poi ON poi.id=pri.purchase_order_item_id WHERE poi.purchase_order_id=@Id;", new { Id = id }, tx, ct));
            if (received > 0) throw FlowHearthValidationException.For("status", "已有收货记录的采购单不能取消。");
        }
        var affected = await connection.ExecuteAsync(Command("UPDATE purchase_orders SET status=@Status,version=version+1,updated_at_utc=@Now,updated_by_user_id=@Actor WHERE id=@Id AND version=@Version;", new { Status = target.ToString(), Now = now, Actor = actor, Id = id, Version = version }, tx, ct));
        if (affected != 1) { await tx.RollbackAsync(ct); return null; }
        await AuditAsync(connection, tx, actor, action, id, before.Code, $"{verb}采购单 {before.Code}", new { status = before.Status, version }, new { status = target.ToString(), version = version + 1 }, now, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    private static async Task<PurchaseOrderDetails?> GetAsync(DbConnection connection, DbTransaction? tx, ulong id, CancellationToken ct)
    {
        var sql = SummarySelect + "\n" + """
            WHERE po.id=@Id;
            SELECT poi.id AS Id,poi.item_name AS ItemName,poi.manufacturer AS Manufacturer,poi.model AS Model,poi.specification AS Specification,poi.quantity AS Quantity,poi.unit AS Unit,poi.unit_price AS UnitPrice,poi.amount AS Amount,COALESCE(SUM(pri.quantity_received),0) AS ReceivedQuantity,poi.quantity-COALESCE(SUM(pri.quantity_received),0) AS RemainingQuantity,CASE WHEN COALESCE(SUM(pri.quantity_received),0)=0 THEN 'NotReceived' WHEN COALESCE(SUM(pri.quantity_received),0)=poi.quantity THEN 'Received' ELSE 'PartiallyReceived' END AS ReceiptStatus,poi.remark AS Remark,poi.version AS Version FROM purchase_order_items poi LEFT JOIN purchase_receipt_items pri ON pri.purchase_order_item_id=poi.id WHERE poi.purchase_order_id=@Id AND poi.deleted_at_utc IS NULL GROUP BY poi.id ORDER BY poi.id;
            SELECT pr.id AS Id,pr.purchase_receipt_code AS Code,pr.purchase_order_id AS PurchaseOrderId,pr.received_date AS ReceivedDate,pr.received_by_user_id AS ReceivedByUserId,u.display_name AS ReceivedByDisplayName,pr.remark AS Remark,pr.created_at_utc AS CreatedAtUtc FROM purchase_receipts pr INNER JOIN users u ON u.id=pr.received_by_user_id WHERE pr.purchase_order_id=@Id ORDER BY pr.received_date DESC,pr.id DESC;
            SELECT pri.id AS Id,pri.purchase_receipt_id AS PurchaseReceiptId,pri.purchase_order_item_id AS PurchaseOrderItemId,poi.item_name AS ItemName,poi.unit AS Unit,pri.quantity_received AS QuantityReceived FROM purchase_receipt_items pri INNER JOIN purchase_receipts pr ON pr.id=pri.purchase_receipt_id INNER JOIN purchase_order_items poi ON poi.id=pri.purchase_order_item_id WHERE pr.purchase_order_id=@Id ORDER BY pri.id;
            """;
        using var result = await connection.QueryMultipleAsync(Command(sql, new { Id = id }, tx, ct));
        var header = await result.ReadSingleOrDefaultAsync<DetailsRow>();
        if (header is null) return null;
        var items = (await result.ReadAsync<PurchaseOrderItemDetails>()).ToArray();
        var receipts = (await result.ReadAsync<ReceiptRow>()).ToArray();
        var receiptItems = (await result.ReadAsync<ReceiptItemRow>()).ToLookup(x => x.PurchaseReceiptId);
        return MapDetails(header, items, receipts.Select(x => new PurchaseReceiptDetails(x.Id, x.Code, x.PurchaseOrderId, x.ReceivedDate, x.ReceivedByUserId, x.ReceivedByDisplayName, x.Remark, x.CreatedAtUtc, receiptItems[x.Id].Select(y => new PurchaseReceiptItemDetails(y.Id, y.PurchaseOrderItemId, y.ItemName, y.Unit, y.QuantityReceived)).ToArray())).ToArray());
    }

    private static async Task ValidateOwnersAsync(DbConnection c, DbTransaction tx, ulong supplierId, ulong projectId, CancellationToken ct)
    {
        var supplier = await c.QuerySingleOrDefaultAsync<OwnerRow>(Command("SELECT id AS Id,status AS Status,archived_at_utc AS ArchivedAtUtc FROM suppliers WHERE id=@Id FOR SHARE;", new { Id = supplierId }, tx, ct));
        if (supplier is null || supplier.ArchivedAtUtc is not null || supplier.Status != "Active") throw FlowHearthValidationException.For("supplierId", "供应商不存在、已停用或已归档。");
        var project = await c.QuerySingleOrDefaultAsync<OwnerRow>(Command("SELECT id AS Id,archived_at_utc AS ArchivedAtUtc FROM projects WHERE id=@Id FOR SHARE;", new { Id = projectId }, tx, ct));
        if (project is null || project.ArchivedAtUtc is not null) throw FlowHearthValidationException.For("projectId", "项目不存在或已归档。");
    }

    private static async Task InsertItemAsync(DbConnection c, DbTransaction tx, ulong orderId, PurchaseOrderItemCommand item, ulong actor, DateTime now, CancellationToken ct) => await c.ExecuteAsync(Command(
        """INSERT INTO purchase_order_items (purchase_order_id,item_name,manufacturer,model,specification,quantity,unit,unit_price,amount,remark,version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id) VALUES (@OrderId,@ItemName,@Manufacturer,@Model,@Specification,@Quantity,@Unit,@UnitPrice,@Amount,@Remark,1,@Now,@Actor,@Now,@Actor);""",
        new { OrderId = orderId, item.ItemName, item.Manufacturer, item.Model, item.Specification, item.Quantity, item.Unit, item.UnitPrice, Amount = PurchaseOrderPolicy.CalculateItemAmount(item.Quantity, item.UnitPrice), item.Remark, Now = now, Actor = actor }, tx, ct));

    private static async Task<ulong> NextSequenceAsync(DbConnection c, DbTransaction tx, string name, DateTime now, CancellationToken ct) => await c.QuerySingleAsync<ulong>(Command(
        """INSERT INTO number_sequences (sequence_name,current_value,version,updated_at_utc) VALUES (@Name,LAST_INSERT_ID(1),1,@Now) ON DUPLICATE KEY UPDATE current_value=LAST_INSERT_ID(current_value+1),version=version+1,updated_at_utc=VALUES(updated_at_utc); SELECT LAST_INSERT_ID();""", new { Name = name, Now = now }, tx, ct));

    private static Task<OrderLockRow?> LockOrderAsync(DbConnection c, DbTransaction tx, ulong id, CancellationToken ct) => c.QuerySingleOrDefaultAsync<OrderLockRow>(Command("SELECT id AS Id,purchase_order_code AS Code,supplier_id AS SupplierId,project_id AS ProjectId,status AS Status,version AS Version,archived_at_utc AS ArchivedAtUtc FROM purchase_orders WHERE id=@Id FOR UPDATE;", new { Id = id }, tx, ct));
    private static Task<TotalsRow> GetTotalsAsync(DbConnection c, DbTransaction tx, ulong id, CancellationToken ct) => c.QuerySingleAsync<TotalsRow>(Command("SELECT SUM(poi.amount) AS TotalAmount,SUM(poi.quantity) AS OrderedQuantity,SUM(COALESCE(r.ReceivedQuantity,0)) AS ReceivedQuantity FROM purchase_order_items poi LEFT JOIN (SELECT purchase_order_item_id,SUM(quantity_received) AS ReceivedQuantity FROM purchase_receipt_items GROUP BY purchase_order_item_id) r ON r.purchase_order_item_id=poi.id WHERE poi.purchase_order_id=@Id AND poi.deleted_at_utc IS NULL;", new { Id = id }, tx, ct));

    private static Task<int> AuditAsync(DbConnection c, DbTransaction tx, ulong actor, string action, ulong id, string code, string summary, object? before, object? after, DateTime now, CancellationToken ct, string entityType = "purchase_order") => c.ExecuteAsync(Command(
        """INSERT INTO audit_logs (occurred_at_utc,actor_user_id,action,entity_type,entity_id,entity_code,summary,before_json,after_json) VALUES (@Now,@Actor,@Action,@EntityType,@Id,@Code,@Summary,@Before,@After);""",
        new { Now = now, Actor = actor, Action = action, EntityType = entityType, Id = id, Code = code, Summary = summary, Before = before is null ? null : JsonSerializer.Serialize(before), After = after is null ? null : JsonSerializer.Serialize(after) }, tx, ct));
    private static object Snapshot(PurchaseOrderWriteData d, decimal total, string status) => new { d.SupplierId, d.ProjectId, d.OrderDate, status, total, d.ContactName, d.DeliveryAddress, d.ExpectedDeliveryDate, d.Remark, items = d.Items.Select(x => new { x.Id, x.ItemName, x.Manufacturer, x.Model, x.Specification, x.Quantity, x.Unit, x.UnitPrice, amount = PurchaseOrderPolicy.CalculateItemAmount(x.Quantity, x.UnitPrice), x.Remark }) };
    private static string GetOrderBy(string sort, bool desc) { var d = desc ? "DESC" : "ASC"; return sort.ToLowerInvariant() switch { "code" => $"po.purchase_order_code {d},po.id {d}", "orderdate" => $"po.order_date {d},po.id {d}", "totalamount" => $"po.total_amount {d},po.id {d}", "status" => $"po.status {d},po.id {d}", "expecteddeliverydate" => $"po.expected_delivery_date {d},po.id {d}", _ => $"po.updated_at_utc {d},po.id {d}" }; }
    private static CommandDefinition Command(string sql, object? p, DbTransaction? tx, CancellationToken ct) => new(sql, p, tx, cancellationToken: ct);
    private static PurchaseOrderSummary MapSummary(SummaryRow r) => new(r.Id, r.Code, r.SupplierId, r.SupplierCode, r.SupplierName, r.ProjectId, r.ProjectCode, r.ProjectName, r.OrderDate, Enum.Parse<PurchaseOrderStatus>(r.Status), r.TotalAmount, r.ExpectedDeliveryDate, r.OrderedQuantity, r.ReceivedQuantity, r.ReceiptProgress, r.ItemCount, r.ReceivedItemCount, r.PartiallyReceivedItemCount, r.NotReceivedItemCount, r.CreatedByDisplayName, r.IsArchived, r.Version, r.UpdatedAtUtc);
    private static PurchaseOrderDetails MapDetails(DetailsRow r, IReadOnlyList<PurchaseOrderItemDetails> items, IReadOnlyList<PurchaseReceiptDetails> receipts) => new(r.Id, r.Code, r.SupplierId, r.SupplierCode, r.SupplierName, r.ProjectId, r.ProjectCode, r.ProjectName, r.OrderDate, Enum.Parse<PurchaseOrderStatus>(r.Status), r.TotalAmount, r.ContactName, r.DeliveryAddress, r.ExpectedDeliveryDate, r.Remark, r.OrderedQuantity, r.ReceivedQuantity, r.ReceiptProgress, r.ItemCount, r.ReceivedItemCount, r.PartiallyReceivedItemCount, r.NotReceivedItemCount, r.CreatedByDisplayName, r.IsArchived, r.ArchivedAtUtc, r.Version, r.CreatedAtUtc, r.UpdatedAtUtc, items, receipts);

    private class SummaryRow { public ulong Id { get; init; } public string Code { get; init; } = ""; public ulong SupplierId { get; init; } public string SupplierCode { get; init; } = ""; public string SupplierName { get; init; } = ""; public ulong ProjectId { get; init; } public string ProjectCode { get; init; } = ""; public string ProjectName { get; init; } = ""; public DateOnly OrderDate { get; init; } public string Status { get; init; } = ""; public decimal TotalAmount { get; init; } public DateOnly? ExpectedDeliveryDate { get; init; } public decimal OrderedQuantity { get; init; } public decimal ReceivedQuantity { get; init; } public decimal ReceiptProgress { get; init; } public int ItemCount { get; init; } public int ReceivedItemCount { get; init; } public int PartiallyReceivedItemCount { get; init; } public int NotReceivedItemCount { get; init; } public string? CreatedByDisplayName { get; init; } public bool IsArchived { get; init; } public ulong Version { get; init; } public DateTime UpdatedAtUtc { get; init; } }
    private sealed class DetailsRow : SummaryRow { public string? ContactName { get; init; } public string? DeliveryAddress { get; init; } public string? Remark { get; init; } public DateTime? ArchivedAtUtc { get; init; } public DateTime CreatedAtUtc { get; init; } }
    private sealed class ReceiptRow { public ulong Id { get; init; } public string Code { get; init; } = ""; public ulong PurchaseOrderId { get; init; } public DateOnly ReceivedDate { get; init; } public ulong ReceivedByUserId { get; init; } public string ReceivedByDisplayName { get; init; } = ""; public string? Remark { get; init; } public DateTime CreatedAtUtc { get; init; } }
    private sealed class ReceiptItemRow { public ulong Id { get; init; } public ulong PurchaseReceiptId { get; init; } public ulong PurchaseOrderItemId { get; init; } public string ItemName { get; init; } = ""; public string Unit { get; init; } = ""; public decimal QuantityReceived { get; init; } }
    private sealed class OrderLockRow { public ulong Id { get; init; } public string Code { get; init; } = ""; public ulong SupplierId { get; init; } public ulong ProjectId { get; init; } public string Status { get; init; } = ""; public ulong Version { get; init; } public DateTime? ArchivedAtUtc { get; init; } }
    private sealed class ItemLockRow { public ulong Id { get; init; } public string ItemName { get; init; } = ""; public string? Manufacturer { get; init; } public string? Model { get; init; } public string? Specification { get; init; } public decimal Quantity { get; init; } public string Unit { get; init; } = ""; public decimal UnitPrice { get; init; } public decimal ReceivedQuantity { get; init; } }
    private sealed class TotalsRow { public decimal TotalAmount { get; init; } public decimal OrderedQuantity { get; init; } public decimal ReceivedQuantity { get; init; } }
    private sealed class OwnerRow { public ulong Id { get; init; } public string? Status { get; init; } public DateTime? ArchivedAtUtc { get; init; } }
}
