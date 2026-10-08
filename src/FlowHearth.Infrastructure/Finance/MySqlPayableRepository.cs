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

public sealed class MySqlPayableRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IPayableRepository
{
    public async Task<PagedResult<PayableSummary>> ListPayablesAsync(
        PayableListCriteria criteria,
        CancellationToken cancellationToken)
    {
        var orderBy = PayableOrderBy(criteria.SortBy, criteria.SortDescending);
        var sql =
            $$"""
            WITH allocation_totals AS
            (
                SELECT payable_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations
                WHERE cancelled_at_utc IS NULL
                GROUP BY payable_id
            ),
            payable_rows AS
            (
                SELECT ap.id AS Id,ap.payable_code AS Code,
                       ap.supplier_id AS SupplierId,s.supplier_code AS SupplierCode,
                       s.name AS SupplierName,ap.project_id AS ProjectId,
                       p.project_code AS ProjectCode,p.name AS ProjectName,
                       ap.purchase_order_id AS PurchaseOrderId,
                       po.purchase_order_code AS PurchaseOrderCode,
                       ap.payable_type AS PayableType,ap.title AS Title,
                       ap.amount AS Amount,COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                       ap.amount-COALESCE(a.AllocatedAmount,0) AS RemainingAmount,
                       ap.due_date AS DueDate,
                       CASE WHEN COALESCE(a.AllocatedAmount,0)=0 THEN 'Unpaid'
                            WHEN COALESCE(a.AllocatedAmount,0)=ap.amount THEN 'Paid'
                            ELSE 'PartiallyPaid' END AS PaymentStatus,
                       (ap.due_date<@BusinessDate
                        AND COALESCE(a.AllocatedAmount,0)<ap.amount) AS IsOverdue,
                       CASE WHEN ap.due_date<@BusinessDate
                                  AND COALESCE(a.AllocatedAmount,0)<ap.amount
                            THEN DATEDIFF(@BusinessDate,ap.due_date) ELSE 0 END AS OverdueDays,
                       (ap.archived_at_utc IS NOT NULL) AS IsArchived,
                       ap.version AS Version,ap.created_at_utc AS CreatedAtUtc,
                       ap.updated_at_utc AS UpdatedAtUtc,
                       ap.remark AS Remark,ap.archived_at_utc AS ArchivedAtUtc
                FROM payables ap
                INNER JOIN suppliers s ON s.id=ap.supplier_id
                INNER JOIN projects p ON p.id=ap.project_id
                LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
                LEFT JOIN allocation_totals a ON a.payable_id=ap.id
                WHERE (@ArchiveMode=2
                       OR (@ArchiveMode=0 AND ap.archived_at_utc IS NULL)
                       OR (@ArchiveMode=1 AND ap.archived_at_utc IS NOT NULL))
                  AND (@SupplierId IS NULL OR ap.supplier_id=@SupplierId)
                  AND (@ProjectId IS NULL OR ap.project_id=@ProjectId)
                  AND (@PurchaseOrderId IS NULL OR ap.purchase_order_id=@PurchaseOrderId)
                  AND (@PayableType IS NULL OR ap.payable_type=@PayableType)
                  AND (@DueFrom IS NULL OR ap.due_date>=@DueFrom)
                  AND (@DueTo IS NULL OR ap.due_date<=@DueTo)
                  AND (@OverdueOnly IS NULL
                       OR (@OverdueOnly=1 AND ap.due_date<@BusinessDate
                           AND COALESCE(a.AllocatedAmount,0)<ap.amount)
                       OR (@OverdueOnly=0 AND (ap.due_date>=@BusinessDate
                           OR COALESCE(a.AllocatedAmount,0)=ap.amount)))
                  AND (@SearchPattern IS NULL
                       OR CONVERT(ap.payable_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR ap.title LIKE @SearchPattern ESCAPE '='
                       OR s.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR p.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(po.purchase_order_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=')
            )
            SELECT * FROM payable_rows
            WHERE @PaymentStatus IS NULL OR PaymentStatus=@PaymentStatus
            ORDER BY {{orderBy}}
            LIMIT @PageSize OFFSET @Offset;

            WITH allocation_totals AS
            (
                SELECT payable_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations
                WHERE cancelled_at_utc IS NULL
                GROUP BY payable_id
            ),
            payable_rows AS
            (
                SELECT ap.id,
                       CASE WHEN COALESCE(a.AllocatedAmount,0)=0 THEN 'Unpaid'
                            WHEN COALESCE(a.AllocatedAmount,0)=ap.amount THEN 'Paid'
                            ELSE 'PartiallyPaid' END AS PaymentStatus
                FROM payables ap
                INNER JOIN suppliers s ON s.id=ap.supplier_id
                INNER JOIN projects p ON p.id=ap.project_id
                LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
                LEFT JOIN allocation_totals a ON a.payable_id=ap.id
                WHERE (@ArchiveMode=2
                       OR (@ArchiveMode=0 AND ap.archived_at_utc IS NULL)
                       OR (@ArchiveMode=1 AND ap.archived_at_utc IS NOT NULL))
                  AND (@SupplierId IS NULL OR ap.supplier_id=@SupplierId)
                  AND (@ProjectId IS NULL OR ap.project_id=@ProjectId)
                  AND (@PurchaseOrderId IS NULL OR ap.purchase_order_id=@PurchaseOrderId)
                  AND (@PayableType IS NULL OR ap.payable_type=@PayableType)
                  AND (@DueFrom IS NULL OR ap.due_date>=@DueFrom)
                  AND (@DueTo IS NULL OR ap.due_date<=@DueTo)
                  AND (@OverdueOnly IS NULL
                       OR (@OverdueOnly=1 AND ap.due_date<@BusinessDate
                           AND COALESCE(a.AllocatedAmount,0)<ap.amount)
                       OR (@OverdueOnly=0 AND (ap.due_date>=@BusinessDate
                           OR COALESCE(a.AllocatedAmount,0)=ap.amount)))
                  AND (@SearchPattern IS NULL
                       OR CONVERT(ap.payable_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR ap.title LIKE @SearchPattern ESCAPE '='
                       OR s.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(p.project_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                       OR p.name LIKE @SearchPattern ESCAPE '='
                       OR CONVERT(po.purchase_order_code USING utf8mb4) LIKE @SearchPattern ESCAPE '=')
            )
            SELECT COUNT(*) FROM payable_rows
            WHERE @PaymentStatus IS NULL OR PaymentStatus=@PaymentStatus;
            """;
        var parameters = new
        {
            BusinessDate = criteria.BusinessDate,
            ArchiveMode = (int)criteria.ArchiveMode,
            criteria.SupplierId,
            criteria.ProjectId,
            criteria.PurchaseOrderId,
            PayableType = criteria.PayableType?.ToString(),
            PaymentStatus = criteria.PaymentStatus?.ToString(),
            criteria.DueFrom,
            criteria.DueTo,
            OverdueOnly = criteria.OverdueOnly.HasValue
                ? criteria.OverdueOnly.Value ? 1 : 0
                : (int?)null,
            SearchPattern = MySqlLikePattern.Contains(criteria.Search),
            criteria.PageSize,
            Offset = ((long)criteria.Page - 1) * criteria.PageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<PayableRow>())
            .Select(row => row.ToSummary())
            .ToArray();
        return new PagedResult<PayableSummary>(
            items, criteria.Page, criteria.PageSize, await result.ReadSingleAsync<long>());
    }

    public async Task<PayableDetails?> GetPayableAsync(
        ulong payableId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT ap.id AS Id,ap.payable_code AS Code,
                   ap.supplier_id AS SupplierId,s.supplier_code AS SupplierCode,
                   s.name AS SupplierName,ap.project_id AS ProjectId,
                   p.project_code AS ProjectCode,p.name AS ProjectName,
                   ap.purchase_order_id AS PurchaseOrderId,
                   po.purchase_order_code AS PurchaseOrderCode,
                   ap.payable_type AS PayableType,ap.title AS Title,
                   ap.amount AS Amount,COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   ap.amount-COALESCE(a.AllocatedAmount,0) AS RemainingAmount,
                   ap.due_date AS DueDate,
                   CASE WHEN COALESCE(a.AllocatedAmount,0)=0 THEN 'Unpaid'
                        WHEN COALESCE(a.AllocatedAmount,0)=ap.amount THEN 'Paid'
                        ELSE 'PartiallyPaid' END AS PaymentStatus,
                   (ap.due_date<@BusinessDate
                    AND COALESCE(a.AllocatedAmount,0)<ap.amount) AS IsOverdue,
                   CASE WHEN ap.due_date<@BusinessDate
                              AND COALESCE(a.AllocatedAmount,0)<ap.amount
                        THEN DATEDIFF(@BusinessDate,ap.due_date) ELSE 0 END AS OverdueDays,
                   ap.remark AS Remark,(ap.archived_at_utc IS NOT NULL) AS IsArchived,
                   ap.archived_at_utc AS ArchivedAtUtc,ap.version AS Version,
                   ap.created_at_utc AS CreatedAtUtc,ap.updated_at_utc AS UpdatedAtUtc
            FROM payables ap
            INNER JOIN suppliers s ON s.id=ap.supplier_id
            INNER JOIN projects p ON p.id=ap.project_id
            LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
            LEFT JOIN
            (
                SELECT payable_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations WHERE cancelled_at_utc IS NULL
                GROUP BY payable_id
            ) a ON a.payable_id=ap.id
            WHERE ap.id=@PayableId;

            SELECT a.id AS Id,a.payment_id AS PaymentId,pm.payment_code AS PaymentCode,
                   pm.payment_date AS PaymentDate,pm.amount AS PaymentAmount,
                   pm.payment_method AS PaymentMethod,a.payable_id AS PayableId,
                   ap.payable_code AS PayableCode,ap.payable_type AS PayableType,
                   ap.title AS PayableTitle,ap.amount AS PayableAmount,
                   ap.project_id AS ProjectId,p.project_code AS ProjectCode,
                   p.name AS ProjectName,ap.purchase_order_id AS PurchaseOrderId,
                   po.purchase_order_code AS PurchaseOrderCode,
                   a.allocated_amount AS AllocatedAmount,
                   creator.display_name AS CreatedByDisplayName,
                   (a.cancelled_at_utc IS NOT NULL) AS IsCancelled,
                   canceller.display_name AS CancelledByDisplayName,
                   a.version AS Version,a.created_at_utc AS CreatedAtUtc,
                   a.cancelled_at_utc AS CancelledAtUtc
            FROM payment_allocations a
            INNER JOIN payments pm ON pm.id=a.payment_id
            INNER JOIN payables ap ON ap.id=a.payable_id
            INNER JOIN projects p ON p.id=ap.project_id
            LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
            LEFT JOIN users creator ON creator.id=a.created_by_user_id
            LEFT JOIN users canceller ON canceller.id=a.cancelled_by_user_id
            WHERE a.payable_id=@PayableId
            ORDER BY a.created_at_utc DESC,a.id DESC;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new { PayableId = payableId, BusinessDate = businessDate }, null,
                cancellationToken));
        var row = await result.ReadSingleOrDefaultAsync<PayableRow>();
        if (row is null)
        {
            return null;
        }

        var allocations = (await result.ReadAsync<PaymentAllocationRow>())
            .Select(item => item.ToDetails())
            .ToArray();
        return row.ToDetails(allocations);
    }

    public async Task<PayableDetails> CreatePayableAsync(
        PayableWriteData data,
        CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await ValidatePayableOwnersAsync(connection, transaction, data, null, cancellationToken);
        var code = FinanceCodes.Format(
            runtime.NumberPrefixes.Payable,
            data.NowUtc.Year,
            await NextSequenceAsync(connection, transaction, "payable", data.NowUtc,
                cancellationToken));
        var id = await InsertPayableAsync(
            connection, transaction, code, data.SupplierId, data.ProjectId,
            data.PurchaseOrderId, data.PayableType, data.Title, data.Amount,
            data.DueDate, data.Remark, data.ActorUserId, data.NowUtc, cancellationToken);
        await AuditAsync(
            connection, transaction, data.ActorUserId, "payable.created", "payable",
            id, code, $"创建应付 {data.Title}，金额 {data.Amount:F2}", null,
            PayableSnapshot(data, code), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPayableAsync(id, data.BusinessDate, cancellationToken)
            ?? throw new InvalidOperationException("Created payable could not be loaded.");
    }

    public async Task<PayableDetails?> UpdatePayableAsync(
        ulong payableId,
        PayableWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var before = await LoadPayableLockAsync(
            connection, transaction, payableId, cancellationToken);
        if (before is null || before.Version != data.Version || before.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (data.Amount < before.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "应付金额不能小于已核销金额。");
        }

        if (before.AllocatedAmount > 0
            && (before.SupplierId != data.SupplierId
                || before.ProjectId != data.ProjectId
                || before.PurchaseOrderId != data.PurchaseOrderId))
        {
            throw FlowHearthValidationException.For(
                "supplierId", "已有付款核销时不能修改供应商、项目或采购单关系。");
        }

        await ValidatePayableOwnersAsync(
            connection, transaction, data, payableId, cancellationToken);
        const string sql =
            """
            UPDATE payables
            SET supplier_id=@SupplierId,project_id=@ProjectId,
                purchase_order_id=@PurchaseOrderId,title=@Title,
                payable_type=@PayableType,amount=@Amount,due_date=@DueDate,
                remark=@Remark,version=version+1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@PayableId AND version=@Version AND archived_at_utc IS NULL;
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    PayableId = payableId,
                    data.SupplierId,
                    data.ProjectId,
                    data.PurchaseOrderId,
                    data.Title,
                    PayableType = data.PayableType.ToString(),
                    data.Amount,
                    data.DueDate,
                    data.Remark,
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

        await AuditAsync(
            connection, transaction, data.ActorUserId, "payable.updated", "payable",
            payableId, before.Code,
            $"修改应付 {before.Code}，金额 {before.Amount:F2} → {data.Amount:F2}，到期日 {before.DueDate:yyyy-MM-dd} → {data.DueDate:yyyy-MM-dd}",
            before, PayableSnapshot(data, before.Code), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPayableAsync(payableId, data.BusinessDate, cancellationToken);
    }

    public async Task<PayableDetails?> SetPayableArchivedAsync(
        ulong payableId,
        SetFinanceArchiveData data,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadPayableLockAsync(
            connection, transaction, payableId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        const string sql =
            """
            UPDATE payables
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version+1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@PayableId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL))
              AND (@Archived=0 OR NOT EXISTS
                  (SELECT 1 FROM payment_allocations a
                   WHERE a.payable_id=payables.id AND a.cancelled_at_utc IS NULL));
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    PayableId = payableId,
                    Archived = data.Archived ? 1 : 0,
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

        await AuditAsync(
            connection, transaction, data.ActorUserId,
            data.Archived ? "payable.archived" : "payable.restored", "payable",
            payableId, before.Code,
            $"{(data.Archived ? "归档" : "恢复")}应付 {before.Code}",
            new { archived = !data.Archived, before.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPayableAsync(payableId, businessDate, cancellationToken);
    }

    public async Task<IReadOnlyList<PayableDetails>> CreatePayablePlanAsync(
        ulong purchaseOrderId,
        PayablePlanWriteData data,
        CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var purchase = await connection.QuerySingleOrDefaultAsync<PurchasePlanRow>(
            Command(
                """
                SELECT po.supplier_id AS SupplierId,po.project_id AS ProjectId,
                       po.purchase_order_code AS Code,po.total_amount AS TotalAmount,
                       po.status AS Status,
                       COALESCE((SELECT SUM(ap.amount) FROM payables ap
                                 WHERE ap.purchase_order_id=po.id
                                   AND ap.archived_at_utc IS NULL),0) AS PlannedAmount
                FROM purchase_orders po
                WHERE po.id=@PurchaseOrderId AND po.archived_at_utc IS NULL
                FOR UPDATE;
                """,
                new { PurchaseOrderId = purchaseOrderId }, transaction,
                cancellationToken));
        if (purchase is null)
        {
            throw new NotFoundException("采购单不存在或已归档。");
        }

        if (purchase.Status is "Draft" or "Cancelled")
        {
            throw FlowHearthValidationException.For(
                "purchaseOrderId", "草稿或已取消采购单不能创建应付计划。");
        }

        var batchAmount = data.Items.Sum(item => item.Amount);
        if (purchase.PlannedAmount + batchAmount > purchase.TotalAmount)
        {
            throw FlowHearthValidationException.For("items", "应付计划总额不能超过采购单金额。");
        }

        var createdIds = new List<ulong>(data.Items.Count);
        var created = new List<object>(data.Items.Count);
        foreach (var item in data.Items)
        {
            var code = FinanceCodes.Format(
                runtime.NumberPrefixes.Payable, data.NowUtc.Year,
                await NextSequenceAsync(connection, transaction, "payable", data.NowUtc,
                    cancellationToken));
            var id = await InsertPayableAsync(
                connection, transaction, code, purchase.SupplierId, purchase.ProjectId,
                purchaseOrderId, item.PayableType, item.Title, item.Amount,
                item.DueDate, item.Remark, data.ActorUserId, data.NowUtc,
                cancellationToken);
            var snapshot = new
            {
                code,
                purchase.SupplierId,
                purchase.ProjectId,
                purchaseOrderId,
                payableType = item.PayableType.ToString(),
                item.Title,
                item.Amount,
                item.DueDate,
                item.Remark,
            };
            await AuditAsync(
                connection, transaction, data.ActorUserId, "payable.created", "payable",
                id, code, $"从采购单 {purchase.Code} 创建应付 {item.Title}",
                null, snapshot, data.NowUtc, cancellationToken);
            createdIds.Add(id);
            created.Add(snapshot);
        }

        await AuditAsync(
            connection, transaction, data.ActorUserId, "payable.plan.created",
            "purchase_order", purchaseOrderId, purchase.Code,
            $"创建 {data.Items.Count} 阶段采购应付计划", null,
            new { purchaseOrderId, batchAmount, items = created }, data.NowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new List<PayableDetails>(createdIds.Count);
        foreach (var id in createdIds)
        {
            result.Add(await GetPayableAsync(id, data.BusinessDate, cancellationToken)
                ?? throw new InvalidOperationException("Created payable could not be loaded."));
        }

        return result;
    }

    public async Task<PagedResult<PaymentSummary>> ListPaymentsAsync(
        int page, int pageSize, string? search, FinanceArchiveMode archiveMode,
        ulong? supplierId, DateOnly? dateFrom, DateOnly? dateTo,
        PaymentMethod? paymentMethod, bool? hasUnallocated,
        string sortBy, bool sortDescending, CancellationToken cancellationToken)
    {
        var orderBy = PaymentOrderBy(sortBy, sortDescending);
        var where =
            """
            WHERE (@ArchiveMode=2
                   OR (@ArchiveMode=0 AND pm.archived_at_utc IS NULL)
                   OR (@ArchiveMode=1 AND pm.archived_at_utc IS NOT NULL))
              AND (@SupplierId IS NULL OR pm.supplier_id=@SupplierId)
              AND (@DateFrom IS NULL OR pm.payment_date>=@DateFrom)
              AND (@DateTo IS NULL OR pm.payment_date<=@DateTo)
              AND (@PaymentMethod IS NULL OR pm.payment_method=@PaymentMethod)
              AND (@HasUnallocated IS NULL
                   OR (@HasUnallocated=1 AND pm.amount>COALESCE(a.AllocatedAmount,0))
                   OR (@HasUnallocated=0 AND pm.amount=COALESCE(a.AllocatedAmount,0)))
              AND (@SearchPattern IS NULL
                   OR CONVERT(pm.payment_code USING utf8mb4) LIKE @SearchPattern ESCAPE '='
                   OR s.name LIKE @SearchPattern ESCAPE '='
                   OR pm.bank_reference LIKE @SearchPattern ESCAPE '=')
            """;
        var sql =
            $"""
            SELECT pm.id AS Id,pm.payment_code AS Code,pm.supplier_id AS SupplierId,
                   s.supplier_code AS SupplierCode,s.name AS SupplierName,
                   pm.payment_date AS PaymentDate,pm.amount AS Amount,
                   COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   pm.amount-COALESCE(a.AllocatedAmount,0) AS UnallocatedAmount,
                   pm.payment_method AS PaymentMethod,pm.payee_name AS PayeeName,
                   pm.bank_reference AS BankReference,
                   (pm.archived_at_utc IS NOT NULL) AS IsArchived,
                   pm.version AS Version,pm.updated_at_utc AS UpdatedAtUtc
            FROM payments pm
            INNER JOIN suppliers s ON s.id=pm.supplier_id
            LEFT JOIN
            (
                SELECT payment_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations WHERE cancelled_at_utc IS NULL
                GROUP BY payment_id
            ) a ON a.payment_id=pm.id
            {where}
            ORDER BY {orderBy}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM payments pm
            INNER JOIN suppliers s ON s.id=pm.supplier_id
            LEFT JOIN
            (
                SELECT payment_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations WHERE cancelled_at_utc IS NULL
                GROUP BY payment_id
            ) a ON a.payment_id=pm.id
            {where};
            """;
        var parameters = new
        {
            ArchiveMode = (int)archiveMode,
            SupplierId = supplierId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            PaymentMethod = paymentMethod?.ToString(),
            HasUnallocated = hasUnallocated.HasValue
                ? hasUnallocated.Value ? 1 : 0
                : (int?)null,
            SearchPattern = MySqlLikePattern.Contains(search),
            PageSize = pageSize,
            Offset = ((long)page - 1) * pageSize,
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, parameters, null, cancellationToken));
        var items = (await result.ReadAsync<PaymentRow>())
            .Select(item => item.ToSummary())
            .ToArray();
        return new PagedResult<PaymentSummary>(
            items, page, pageSize, await result.ReadSingleAsync<long>());
    }

    public async Task<PaymentDetails?> GetPaymentAsync(
        ulong paymentId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT pm.id AS Id,pm.payment_code AS Code,pm.supplier_id AS SupplierId,
                   s.supplier_code AS SupplierCode,s.name AS SupplierName,
                   pm.payment_date AS PaymentDate,pm.amount AS Amount,
                   COALESCE(a.AllocatedAmount,0) AS AllocatedAmount,
                   pm.amount-COALESCE(a.AllocatedAmount,0) AS UnallocatedAmount,
                   pm.payment_method AS PaymentMethod,pm.payee_name AS PayeeName,
                   pm.bank_reference AS BankReference,pm.remark AS Remark,
                   (pm.archived_at_utc IS NOT NULL) AS IsArchived,
                   pm.archived_at_utc AS ArchivedAtUtc,pm.version AS Version,
                   pm.created_at_utc AS CreatedAtUtc,pm.updated_at_utc AS UpdatedAtUtc
            FROM payments pm
            INNER JOIN suppliers s ON s.id=pm.supplier_id
            LEFT JOIN
            (
                SELECT payment_id,SUM(allocated_amount) AS AllocatedAmount
                FROM payment_allocations WHERE cancelled_at_utc IS NULL
                GROUP BY payment_id
            ) a ON a.payment_id=pm.id
            WHERE pm.id=@PaymentId;

            SELECT a.id AS Id,a.payment_id AS PaymentId,pm.payment_code AS PaymentCode,
                   pm.payment_date AS PaymentDate,pm.amount AS PaymentAmount,
                   pm.payment_method AS PaymentMethod,a.payable_id AS PayableId,
                   ap.payable_code AS PayableCode,ap.payable_type AS PayableType,
                   ap.title AS PayableTitle,ap.amount AS PayableAmount,
                   ap.project_id AS ProjectId,p.project_code AS ProjectCode,
                   p.name AS ProjectName,ap.purchase_order_id AS PurchaseOrderId,
                   po.purchase_order_code AS PurchaseOrderCode,
                   a.allocated_amount AS AllocatedAmount,
                   creator.display_name AS CreatedByDisplayName,
                   (a.cancelled_at_utc IS NOT NULL) AS IsCancelled,
                   canceller.display_name AS CancelledByDisplayName,
                   a.version AS Version,a.created_at_utc AS CreatedAtUtc,
                   a.cancelled_at_utc AS CancelledAtUtc
            FROM payment_allocations a
            INNER JOIN payments pm ON pm.id=a.payment_id
            INNER JOIN payables ap ON ap.id=a.payable_id
            INNER JOIN projects p ON p.id=ap.project_id
            LEFT JOIN purchase_orders po ON po.id=ap.purchase_order_id
            LEFT JOIN users creator ON creator.id=a.created_by_user_id
            LEFT JOIN users canceller ON canceller.id=a.cancelled_by_user_id
            WHERE a.payment_id=@PaymentId
            ORDER BY a.created_at_utc,a.id;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new { PaymentId = paymentId }, null, cancellationToken));
        var header = await result.ReadSingleOrDefaultAsync<PaymentRow>();
        if (header is null)
        {
            return null;
        }

        var allocations = (await result.ReadAsync<PaymentAllocationRow>())
            .Select(item => item.ToDetails())
            .ToArray();
        return header.ToDetails(allocations);
    }

    public async Task<PaymentDetails> CreatePaymentAsync(
        PaymentWriteData data,
        CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await ValidatePaymentSupplierAsync(
            connection, transaction, data.SupplierId, cancellationToken);
        var code = FinanceCodes.Format(
            runtime.NumberPrefixes.Payment, data.NowUtc.Year,
            await NextSequenceAsync(connection, transaction, "payment", data.NowUtc,
                cancellationToken));
        const string sql =
            """
            INSERT INTO payments
                (payment_code,supplier_id,payment_date,amount,payment_method,
                 payee_name,bank_reference,remark,version,created_at_utc,
                 created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@SupplierId,@PaymentDate,@Amount,@PaymentMethod,
                 @PayeeName,@BankReference,@Remark,1,@NowUtc,@ActorUserId,
                 @NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        var id = await connection.QuerySingleAsync<ulong>(
            Command(sql, PaymentParameters(data, code), transaction, cancellationToken));
        await AuditAsync(
            connection, transaction, data.ActorUserId, "payment.created", "payment",
            id, code, $"创建付款 {code}，金额 {data.Amount:F2}", null,
            PaymentSnapshot(data, code), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Created payment could not be loaded.");
    }

    public async Task<PaymentDetails?> UpdatePaymentAsync(
        ulong paymentId,
        PaymentWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var before = await LoadPaymentLockAsync(
            connection, transaction, paymentId, cancellationToken);
        if (before is null || before.Version != data.Version || before.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (data.Amount < before.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "付款金额不能小于已核销金额。");
        }

        if (before.AllocatedAmount > 0 && before.SupplierId != data.SupplierId)
        {
            throw FlowHearthValidationException.For("supplierId", "已有付款核销时不能更换供应商。");
        }

        await ValidatePaymentSupplierAsync(
            connection, transaction, data.SupplierId, cancellationToken);
        const string sql =
            """
            UPDATE payments
            SET supplier_id=@SupplierId,payment_date=@PaymentDate,amount=@Amount,
                payment_method=@PaymentMethod,payee_name=@PayeeName,
                bank_reference=@BankReference,remark=@Remark,version=version+1,
                updated_at_utc=@NowUtc,updated_by_user_id=@ActorUserId
            WHERE id=@PaymentId AND version=@Version AND archived_at_utc IS NULL;
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    PaymentId = paymentId,
                    data.SupplierId,
                    data.PaymentDate,
                    data.Amount,
                    PaymentMethod = data.PaymentMethod.ToString(),
                    data.PayeeName,
                    data.BankReference,
                    data.Remark,
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

        await AuditAsync(
            connection, transaction, data.ActorUserId, "payment.updated", "payment",
            paymentId, before.Code,
            $"修改付款 {before.Code}，金额 {before.Amount:F2} → {data.Amount:F2}",
            before, PaymentSnapshot(data, before.Code), data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentDetails?> SetPaymentArchivedAsync(
        ulong paymentId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var before = await LoadPaymentLockAsync(
            connection, transaction, paymentId, cancellationToken);
        if (before is null || before.Version != data.Version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        const string sql =
            """
            UPDATE payments
            SET archived_at_utc=CASE WHEN @Archived=1 THEN @NowUtc ELSE NULL END,
                archived_by_user_id=CASE WHEN @Archived=1 THEN @ActorUserId ELSE NULL END,
                version=version+1,updated_at_utc=@NowUtc,
                updated_by_user_id=@ActorUserId
            WHERE id=@PaymentId AND version=@Version
              AND ((@Archived=1 AND archived_at_utc IS NULL)
                   OR (@Archived=0 AND archived_at_utc IS NOT NULL))
              AND (@Archived=0 OR NOT EXISTS
                  (SELECT 1 FROM payment_allocations a
                   WHERE a.payment_id=payments.id AND a.cancelled_at_utc IS NULL));
            """;
        var affected = await connection.ExecuteAsync(
            Command(
                sql,
                new
                {
                    PaymentId = paymentId,
                    Archived = data.Archived ? 1 : 0,
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

        await AuditAsync(
            connection, transaction, data.ActorUserId,
            data.Archived ? "payment.archived" : "payment.restored", "payment",
            paymentId, before.Code,
            $"{(data.Archived ? "归档" : "恢复")}付款 {before.Code}",
            new { archived = !data.Archived, before.Version },
            new { archived = data.Archived, version = data.Version + 1 },
            data.NowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentDetails?> AllocatePaymentAsync(
        ulong paymentId,
        PaymentAllocationWriteData data,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AllocatePaymentCoreAsync(
                paymentId, data, cancellationToken);
        }
        catch (MySqlConnector.MySqlException exception)
            when (exception.Number is 1205 or 1213)
        {
            return null;
        }
    }

    private async Task<PaymentDetails?> AllocatePaymentCoreAsync(
        ulong paymentId,
        PaymentAllocationWriteData data,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var payment = await LoadPaymentLockAsync(
            connection, transaction, paymentId, cancellationToken);
        if (payment is null || payment.Version != data.PaymentVersion || payment.IsArchived)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var runningAllocated = payment.AllocatedAmount;
        foreach (var item in data.Allocations.OrderBy(item => item.PayableId))
        {
            var payable = await LoadPayableLockAsync(
                connection, transaction, item.PayableId, cancellationToken);
            if (payable is null || payable.IsArchived)
            {
                throw FlowHearthValidationException.For("payableId", "应付账款不存在或已归档。");
            }

            if (payable.SupplierId != payment.SupplierId)
            {
                throw FlowHearthValidationException.For("payableId", "付款和应付必须属于同一供应商。");
            }

            if (item.Amount > payment.Amount - runningAllocated)
            {
                throw FlowHearthValidationException.For("amount", "核销总额超过付款未核销余额。");
            }

            if (item.Amount > payable.Amount - payable.AllocatedAmount)
            {
                throw FlowHearthValidationException.For(
                    "amount", $"核销金额超过应付 {payable.Code} 的未付余额。");
            }

            const string insertSql =
                """
                INSERT INTO payment_allocations
                    (payment_id,payable_id,allocated_amount,version,
                     created_at_utc,created_by_user_id)
                VALUES
                    (@PaymentId,@PayableId,@Amount,1,@NowUtc,@ActorUserId);
                SELECT LAST_INSERT_ID();
                """;
            var allocationId = await connection.QuerySingleAsync<ulong>(
                Command(
                    insertSql,
                    new
                    {
                        PaymentId = paymentId,
                        item.PayableId,
                        item.Amount,
                        data.NowUtc,
                        data.ActorUserId,
                    },
                    transaction,
                    cancellationToken));
            await AuditAsync(
                connection, transaction, data.ActorUserId,
                "payment_allocation.created", "payment_allocation", allocationId,
                payment.Code,
                $"付款 {payment.Code} 核销应付 {payable.Code}，金额 {item.Amount:F2}",
                null,
                new
                {
                    paymentId,
                    item.PayableId,
                    amount = item.Amount,
                    paymentAllocatedBefore = runningAllocated,
                    paymentAllocatedAfter = runningAllocated + item.Amount,
                    payableAllocatedBefore = payable.AllocatedAmount,
                    payableAllocatedAfter = payable.AllocatedAmount + item.Amount,
                },
                data.NowUtc,
                cancellationToken);
            runningAllocated += item.Amount;
        }

        var bumped = await connection.ExecuteAsync(
            Command(
                """
                UPDATE payments
                SET version=version+1,updated_at_utc=@NowUtc,
                    updated_by_user_id=@ActorUserId
                WHERE id=@PaymentId AND version=@Version;
                """,
                new
                {
                    PaymentId = paymentId,
                    Version = data.PaymentVersion,
                    data.NowUtc,
                    data.ActorUserId,
                },
                transaction,
                cancellationToken));
        if (bumped != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentAsync(paymentId, cancellationToken);
    }

    public async Task<PaymentDetails?> CancelPaymentAllocationAsync(
        ulong paymentId,
        ulong allocationId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var payment = await LoadPaymentLockAsync(
            connection, transaction, paymentId, cancellationToken);
        var allocation = await connection.QuerySingleOrDefaultAsync<AllocationLockRow>(
            Command(
                """
                SELECT a.id AS Id,a.payment_id AS PaymentId,a.payable_id AS PayableId,
                       a.allocated_amount AS Amount,a.version AS Version,
                       ap.payable_code AS PayableCode
                FROM payment_allocations a
                INNER JOIN payables ap ON ap.id=a.payable_id
                WHERE a.id=@AllocationId AND a.payment_id=@PaymentId
                  AND a.cancelled_at_utc IS NULL
                FOR UPDATE;
                """,
                new { AllocationId = allocationId, PaymentId = paymentId },
                transaction,
                cancellationToken));
        if (payment is null || allocation is null || allocation.Version != version)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await LoadPayableLockAsync(
            connection, transaction, allocation.PayableId, cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(
                """
                UPDATE payment_allocations
                SET cancelled_at_utc=@NowUtc,cancelled_by_user_id=@ActorUserId,
                    version=version+1
                WHERE id=@AllocationId AND version=@Version
                  AND cancelled_at_utc IS NULL;
                """,
                new
                {
                    AllocationId = allocationId,
                    Version = version,
                    NowUtc = nowUtc,
                    ActorUserId = actorUserId,
                },
                transaction,
                cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(
            Command(
                """
                UPDATE payments
                SET version=version+1,updated_at_utc=@NowUtc,
                    updated_by_user_id=@ActorUserId
                WHERE id=@PaymentId;
                """,
                new { PaymentId = paymentId, NowUtc = nowUtc, ActorUserId = actorUserId },
                transaction,
                cancellationToken));
        await AuditAsync(
            connection, transaction, actorUserId, "payment_allocation.cancelled",
            "payment_allocation", allocationId, payment.Code,
            $"取消付款 {payment.Code} 对应付 {allocation.PayableCode} 的核销 {allocation.Amount:F2}",
            new
            {
                allocation.PaymentId,
                allocation.PayableId,
                amount = allocation.Amount,
                cancelled = false,
            },
            new
            {
                allocation.PaymentId,
                allocation.PayableId,
                amount = allocation.Amount,
                cancelled = true,
            },
            nowUtc,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPaymentAsync(paymentId, cancellationToken);
    }

    private static async Task ValidatePayableOwnersAsync(
        DbConnection connection,
        DbTransaction transaction,
        PayableWriteData data,
        ulong? excludedPayableId,
        CancellationToken cancellationToken)
    {
        var supplier = await connection.QuerySingleOrDefaultAsync<OwnerRow>(
            Command(
                "SELECT id AS Id,status AS Status,archived_at_utc AS ArchivedAtUtc FROM suppliers WHERE id=@Id FOR SHARE;",
                new { Id = data.SupplierId }, transaction, cancellationToken));
        if (supplier is null || supplier.ArchivedAtUtc is not null || supplier.Status != "Active")
        {
            throw FlowHearthValidationException.For("supplierId", "供应商不存在、已停用或已归档。");
        }

        var project = await connection.QuerySingleOrDefaultAsync<OwnerRow>(
            Command(
                "SELECT id AS Id,archived_at_utc AS ArchivedAtUtc FROM projects WHERE id=@Id FOR SHARE;",
                new { Id = data.ProjectId }, transaction, cancellationToken));
        if (project is null || project.ArchivedAtUtc is not null)
        {
            throw FlowHearthValidationException.For("projectId", "项目不存在或已归档。");
        }

        if (!data.PurchaseOrderId.HasValue)
        {
            return;
        }

        var purchase = await connection.QuerySingleOrDefaultAsync<PurchasePlanRow>(
            Command(
                """
                SELECT po.supplier_id AS SupplierId,po.project_id AS ProjectId,
                       po.purchase_order_code AS Code,po.total_amount AS TotalAmount,
                       po.status AS Status,
                       COALESCE((SELECT SUM(ap.amount) FROM payables ap
                                 WHERE ap.purchase_order_id=po.id
                                   AND ap.archived_at_utc IS NULL
                                   AND (@ExcludedId IS NULL OR ap.id<>@ExcludedId)),0)
                           AS PlannedAmount
                FROM purchase_orders po
                WHERE po.id=@PurchaseOrderId AND po.archived_at_utc IS NULL
                FOR UPDATE;
                """,
                new { data.PurchaseOrderId, ExcludedId = excludedPayableId }, transaction,
                cancellationToken));
        if (purchase is null || purchase.Status is "Draft" or "Cancelled")
        {
            throw FlowHearthValidationException.For(
                "purchaseOrderId", "采购单不存在、已归档、处于草稿或已取消。");
        }

        if (purchase.SupplierId != data.SupplierId || purchase.ProjectId != data.ProjectId)
        {
            throw FlowHearthValidationException.For(
                "purchaseOrderId", "采购单、供应商和项目关系不一致。");
        }

        if (purchase.PlannedAmount + data.Amount > purchase.TotalAmount)
        {
            throw FlowHearthValidationException.For("amount", "采购单累计应付不能超过采购金额。");
        }
    }

    private static async Task ValidatePaymentSupplierAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong supplierId,
        CancellationToken cancellationToken)
    {
        var supplier = await connection.QuerySingleOrDefaultAsync<OwnerRow>(
            Command(
                "SELECT id AS Id,status AS Status,archived_at_utc AS ArchivedAtUtc FROM suppliers WHERE id=@Id FOR SHARE;",
                new { Id = supplierId }, transaction, cancellationToken));
        if (supplier is null || supplier.ArchivedAtUtc is not null || supplier.Status != "Active")
        {
            throw FlowHearthValidationException.For("supplierId", "供应商不存在、已停用或已归档。");
        }
    }

    private static Task<PayableLockRow?> LoadPayableLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong payableId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<PayableLockRow>(
            Command(
                """
                SELECT ap.payable_code AS Code,ap.supplier_id AS SupplierId,
                       ap.project_id AS ProjectId,
                       ap.purchase_order_id AS PurchaseOrderId,ap.title AS Title,
                       ap.amount AS Amount,ap.due_date AS DueDate,
                       COALESCE(SUM(CASE WHEN a.cancelled_at_utc IS NULL
                                         THEN a.allocated_amount ELSE 0 END),0)
                           AS AllocatedAmount,
                       (ap.archived_at_utc IS NOT NULL) AS IsArchived,
                       ap.version AS Version
                FROM payables ap
                LEFT JOIN payment_allocations a ON a.payable_id=ap.id
                WHERE ap.id=@PayableId
                GROUP BY ap.id
                FOR UPDATE;
                """,
                new { PayableId = payableId }, transaction, cancellationToken));

    private static Task<PaymentLockRow?> LoadPaymentLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong paymentId,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<PaymentLockRow>(
            Command(
                """
                SELECT pm.payment_code AS Code,pm.supplier_id AS SupplierId,
                       pm.amount AS Amount,
                       COALESCE(SUM(CASE WHEN a.cancelled_at_utc IS NULL
                                         THEN a.allocated_amount ELSE 0 END),0)
                           AS AllocatedAmount,
                       (pm.archived_at_utc IS NOT NULL) AS IsArchived,
                       pm.version AS Version
                FROM payments pm
                LEFT JOIN payment_allocations a ON a.payment_id=pm.id
                WHERE pm.id=@PaymentId
                GROUP BY pm.id
                FOR UPDATE;
                """,
                new { PaymentId = paymentId }, transaction, cancellationToken));

    private static Task<ulong> InsertPayableAsync(
        DbConnection connection, DbTransaction transaction, string code,
        ulong supplierId, ulong projectId, ulong? purchaseOrderId,
        PayableType payableType, string title, decimal amount, DateOnly dueDate,
        string? remark, ulong actorUserId, DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<ulong>(
            Command(
                """
                INSERT INTO payables
                    (payable_code,supplier_id,project_id,purchase_order_id,title,
                     payable_type,amount,due_date,remark,version,created_at_utc,
                     created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES
                    (@Code,@SupplierId,@ProjectId,@PurchaseOrderId,@Title,
                     @PayableType,@Amount,@DueDate,@Remark,1,@NowUtc,@ActorUserId,
                     @NowUtc,@ActorUserId);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = code,
                    SupplierId = supplierId,
                    ProjectId = projectId,
                    PurchaseOrderId = purchaseOrderId,
                    PayableType = payableType.ToString(),
                    Title = title,
                    Amount = amount,
                    DueDate = dueDate,
                    Remark = remark,
                    ActorUserId = actorUserId,
                    NowUtc = nowUtc,
                },
                transaction,
                cancellationToken));

    private static Task<ulong> NextSequenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        string entity,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        connection.QuerySingleAsync<ulong>(
            Command(
                """
                INSERT INTO number_sequences
                    (sequence_name,current_value,version,updated_at_utc)
                VALUES
                    (@SequenceName,LAST_INSERT_ID(1),1,@NowUtc)
                ON DUPLICATE KEY UPDATE
                    current_value=LAST_INSERT_ID(current_value+1),
                    version=version+1,updated_at_utc=VALUES(updated_at_utc);
                SELECT LAST_INSERT_ID();
                """,
                new { SequenceName = $"{entity}:{nowUtc.Year:D4}", NowUtc = nowUtc },
                transaction,
                cancellationToken));

    private static object PaymentParameters(PaymentWriteData data, string code) =>
        new
        {
            Code = code,
            data.SupplierId,
            data.PaymentDate,
            data.Amount,
            PaymentMethod = data.PaymentMethod.ToString(),
            data.PayeeName,
            data.BankReference,
            data.Remark,
            data.NowUtc,
            data.ActorUserId,
        };

    private static object PaymentSnapshot(PaymentWriteData data, string code) =>
        new
        {
            code,
            data.SupplierId,
            data.PaymentDate,
            data.Amount,
            paymentMethod = data.PaymentMethod.ToString(),
            data.PayeeName,
            data.BankReference,
            data.Remark,
        };

    private static object PayableSnapshot(PayableWriteData data, string code) =>
        new
        {
            code,
            data.SupplierId,
            data.ProjectId,
            data.PurchaseOrderId,
            payableType = data.PayableType.ToString(),
            data.Title,
            data.Amount,
            data.DueDate,
            data.Remark,
        };

    private static Task<int> AuditAsync(
        DbConnection connection, DbTransaction transaction, ulong actorUserId,
        string action, string entityType, ulong entityId, string entityCode,
        string summary, object? before, object? after, DateTime occurredAtUtc,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            Command(
                """
                INSERT INTO audit_logs
                    (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                     entity_code,summary,before_json,after_json)
                VALUES
                    (@OccurredAtUtc,@ActorUserId,@Action,@EntityType,@EntityId,
                     @EntityCode,@Summary,@BeforeJson,@AfterJson);
                """,
                new
                {
                    OccurredAtUtc = occurredAtUtc,
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

    private static string PayableOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"Code {direction},Id {direction}",
            "amount" => $"Amount {direction},Id {direction}",
            "duedate" => $"DueDate {direction},Id {direction}",
            "createdat" => $"CreatedAtUtc {direction},Id {direction}",
            "paymentstatus" => $"PaymentStatus {direction},IsOverdue {direction},Id {direction}",
            _ => $"UpdatedAtUtc {direction},Id {direction}",
        };
    }

    private static string PaymentOrderBy(string sortBy, bool descending)
    {
        var direction = descending ? "DESC" : "ASC";
        return sortBy.ToLowerInvariant() switch
        {
            "code" => $"pm.payment_code {direction},pm.id {direction}",
            "amount" => $"pm.amount {direction},pm.id {direction}",
            "paymentdate" => $"pm.payment_date {direction},pm.id {direction}",
            "unallocatedamount" => $"UnallocatedAmount {direction},pm.id {direction}",
            _ => $"pm.updated_at_utc {direction},pm.id {direction}",
        };
    }

    private static CommandDefinition Command(
        string sql, object? parameters, DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private sealed class PayableRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong SupplierId { get; init; }
        public string SupplierCode { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public ulong? PurchaseOrderId { get; init; }
        public string? PurchaseOrderCode { get; init; }
        public string PayableType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public decimal RemainingAmount { get; init; }
        public DateOnly DueDate { get; init; }
        public string PaymentStatus { get; init; } = string.Empty;
        public bool IsOverdue { get; init; }
        public int OverdueDays { get; init; }
        public string? Remark { get; init; }
        public bool IsArchived { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public PayableSummary ToSummary() =>
            new(
                Id, Code, SupplierId, SupplierCode, SupplierName, ProjectId,
                ProjectCode, ProjectName, PurchaseOrderId, PurchaseOrderCode,
                Enum.Parse<PayableType>(PayableType), Title, Amount, AllocatedAmount,
                RemainingAmount, DueDate, Enum.Parse<PaymentStatus>(PaymentStatus),
                IsOverdue, OverdueDays, IsArchived, Version, UpdatedAtUtc);

        public PayableDetails ToDetails(
            IReadOnlyList<PaymentAllocationDetails> allocations) =>
            new(
                Id, Code, SupplierId, SupplierCode, SupplierName, ProjectId,
                ProjectCode, ProjectName, PurchaseOrderId, PurchaseOrderCode,
                Enum.Parse<PayableType>(PayableType), Title, Amount, AllocatedAmount,
                RemainingAmount, DueDate, Enum.Parse<PaymentStatus>(PaymentStatus),
                IsOverdue, OverdueDays, Remark, IsArchived, ArchivedAtUtc, Version,
                CreatedAtUtc, UpdatedAtUtc, allocations);
    }

    private sealed class PaymentRow
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public ulong SupplierId { get; init; }
        public string SupplierCode { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
        public DateOnly PaymentDate { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public decimal UnallocatedAmount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string? PayeeName { get; init; }
        public string? BankReference { get; init; }
        public string? Remark { get; init; }
        public bool IsArchived { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public PaymentSummary ToSummary() =>
            new(
                Id, Code, SupplierId, SupplierCode, SupplierName, PaymentDate,
                Amount, AllocatedAmount, UnallocatedAmount,
                Enum.Parse<PaymentMethod>(PaymentMethod), PayeeName, BankReference,
                IsArchived, Version, UpdatedAtUtc);

        public PaymentDetails ToDetails(
            IReadOnlyList<PaymentAllocationDetails> allocations) =>
            new(
                Id, Code, SupplierId, SupplierCode, SupplierName, PaymentDate,
                Amount, AllocatedAmount, UnallocatedAmount,
                Enum.Parse<PaymentMethod>(PaymentMethod), PayeeName, BankReference,
                Remark, IsArchived, ArchivedAtUtc, Version, CreatedAtUtc,
                UpdatedAtUtc, allocations);
    }

    private sealed class PaymentAllocationRow
    {
        public ulong Id { get; init; }
        public ulong PaymentId { get; init; }
        public string PaymentCode { get; init; } = string.Empty;
        public DateOnly PaymentDate { get; init; }
        public decimal PaymentAmount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public ulong PayableId { get; init; }
        public string PayableCode { get; init; } = string.Empty;
        public string PayableType { get; init; } = string.Empty;
        public string PayableTitle { get; init; } = string.Empty;
        public decimal PayableAmount { get; init; }
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public ulong? PurchaseOrderId { get; init; }
        public string? PurchaseOrderCode { get; init; }
        public decimal AllocatedAmount { get; init; }
        public string? CreatedByDisplayName { get; init; }
        public bool IsCancelled { get; init; }
        public string? CancelledByDisplayName { get; init; }
        public ulong Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? CancelledAtUtc { get; init; }

        public PaymentAllocationDetails ToDetails() =>
            new(
                Id, PaymentId, PaymentCode, PaymentDate, PaymentAmount,
                Enum.Parse<PaymentMethod>(PaymentMethod), PayableId, PayableCode,
                Enum.Parse<PayableType>(PayableType), PayableTitle, PayableAmount,
                ProjectId, ProjectCode, ProjectName, PurchaseOrderId,
                PurchaseOrderCode, AllocatedAmount, CreatedByDisplayName,
                IsCancelled, CancelledByDisplayName, Version, CreatedAtUtc,
                CancelledAtUtc);
    }

    private sealed class PayableLockRow
    {
        public string Code { get; init; } = string.Empty;
        public ulong SupplierId { get; init; }
        public ulong ProjectId { get; init; }
        public ulong? PurchaseOrderId { get; init; }
        public string Title { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public DateOnly DueDate { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class PaymentLockRow
    {
        public string Code { get; init; } = string.Empty;
        public ulong SupplierId { get; init; }
        public decimal Amount { get; init; }
        public decimal AllocatedAmount { get; init; }
        public bool IsArchived { get; init; }
        public ulong Version { get; init; }
    }

    private sealed class AllocationLockRow
    {
        public ulong Id { get; init; }
        public ulong PaymentId { get; init; }
        public ulong PayableId { get; init; }
        public decimal Amount { get; init; }
        public ulong Version { get; init; }
        public string PayableCode { get; init; } = string.Empty;
    }

    private sealed class OwnerRow
    {
        public ulong Id { get; init; }
        public string? Status { get; init; }
        public DateTime? ArchivedAtUtc { get; init; }
    }

    private sealed class PurchasePlanRow
    {
        public ulong SupplierId { get; init; }
        public ulong ProjectId { get; init; }
        public string Code { get; init; } = string.Empty;
        public decimal TotalAmount { get; init; }
        public decimal PlannedAmount { get; init; }
        public string Status { get; init; } = string.Empty;
    }
}
