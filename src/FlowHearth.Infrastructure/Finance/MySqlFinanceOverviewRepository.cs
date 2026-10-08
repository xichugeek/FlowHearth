using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Domain.Projects;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.Infrastructure.Finance;

public sealed class MySqlFinanceOverviewRepository(
    IDbConnectionFactory connectionFactory,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IFinanceOverviewRepository
{
    private static readonly string[] AgingBucketNames =
        ["NotDue", "1-30", "31-60", "61-90", "90+"];

    private const string ReceiptAllocationJoin =
        """
        LEFT JOIN
        (
            SELECT ra.receivable_id,SUM(ra.allocated_amount) AllocatedAmount
            FROM receipt_allocations ra
            INNER JOIN receipts rc ON rc.id=ra.receipt_id
                AND rc.archived_at_utc IS NULL
            WHERE ra.cancelled_at_utc IS NULL
            GROUP BY ra.receivable_id
        ) ar ON ar.receivable_id=r.id
        """;

    private const string PaymentAllocationJoin =
        """
        LEFT JOIN
        (
            SELECT pa.payable_id,SUM(pa.allocated_amount) AllocatedAmount
            FROM payment_allocations pa
            INNER JOIN payments pm ON pm.id=pa.payment_id
                AND pm.archived_at_utc IS NULL
            WHERE pa.cancelled_at_utc IS NULL
            GROUP BY pa.payable_id
        ) aa ON aa.payable_id=ap.id
        """;

    private static readonly string ProjectMetricsSource =
        $$"""
        SELECT p.id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
               c.id AS CustomerId,c.name AS CustomerName,p.status AS ProjectStatus,
               p.contract_amount AS ContractAmount,
               COALESCE(po.PurchaseAmount,0) AS PurchaseAmount,
               p.contract_amount-COALESCE(po.PurchaseAmount,0) AS GrossProfit,
               CASE WHEN p.contract_amount>0 THEN ROUND(
                   (p.contract_amount-COALESCE(po.PurchaseAmount,0)) /
                   p.contract_amount*100,2) END AS GrossMargin,
               COALESCE(r.OutstandingAmount,0) AS ReceivableOutstandingAmount,
               COALESCE(r.OverdueAmount,0) AS ReceivableOverdueAmount
        FROM projects p
        INNER JOIN customers c ON c.id=p.customer_id
        LEFT JOIN
        (
            SELECT project_id,SUM(total_amount) AS PurchaseAmount
            FROM purchase_orders
            WHERE archived_at_utc IS NULL
              AND status IN ('Ordered','PartiallyReceived','Received')
            GROUP BY project_id
        ) po ON po.project_id=p.id
        LEFT JOIN
        (
            SELECT r.project_id,
                   SUM(GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0))
                       AS OutstandingAmount,
                   SUM(CASE WHEN r.due_date<@BusinessDate
                            THEN GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0)
                            ELSE 0 END) AS OverdueAmount
            FROM receivables r
            {{ReceiptAllocationJoin}}
            WHERE r.archived_at_utc IS NULL
            GROUP BY r.project_id
        ) r ON r.project_id=p.id
        WHERE p.archived_at_utc IS NULL AND p.status<>'Cancelled'
        """;

    public async Task<CustomerFinanceAggregateData?> GetCustomerAsync(
        ulong customerId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var sql =
            $$"""
            SELECT c.id AS CustomerId,c.customer_code AS CustomerCode,c.name AS CustomerName,
                   (SELECT COUNT(*) FROM projects p
                    WHERE p.customer_id=c.id AND p.archived_at_utc IS NULL
                      AND p.status<>'Cancelled') AS ProjectCount,
                   (SELECT COUNT(*) FROM projects p
                    WHERE p.customer_id=c.id AND p.archived_at_utc IS NULL
                      AND p.status IN ('Planning','Active','OnHold')) AS ActiveProjectCount,
                   COALESCE((SELECT SUM(p.contract_amount) FROM projects p
                             WHERE p.customer_id=c.id AND p.archived_at_utc IS NULL
                               AND p.status<>'Cancelled'),0) AS ContractAmount,
                   COALESCE((SELECT SUM(rc.amount) FROM receipts rc
                             WHERE rc.customer_id=c.id AND rc.archived_at_utc IS NULL),0)
                       AS ReceiptAmount,
                   COALESCE((SELECT SUM(ra.allocated_amount)
                             FROM receipt_allocations ra
                             INNER JOIN receipts rc ON rc.id=ra.receipt_id
                                AND rc.archived_at_utc IS NULL
                             INNER JOIN receivables r ON r.id=ra.receivable_id
                                AND r.archived_at_utc IS NULL
                             WHERE r.customer_id=c.id AND ra.cancelled_at_utc IS NULL),0)
                       AS ReceivedAllocatedAmount,
                   COALESCE((SELECT SUM(rc.amount-COALESCE(a.AllocatedAmount,0))
                             FROM receipts rc
                             LEFT JOIN
                             (
                                 SELECT ra.receipt_id,SUM(ra.allocated_amount) AllocatedAmount
                                 FROM receipt_allocations ra
                                 INNER JOIN receivables r ON r.id=ra.receivable_id
                                    AND r.archived_at_utc IS NULL
                                 WHERE ra.cancelled_at_utc IS NULL
                                 GROUP BY ra.receipt_id
                             ) a ON a.receipt_id=rc.id
                             WHERE rc.customer_id=c.id AND rc.archived_at_utc IS NULL),0)
                       AS UnallocatedReceiptAmount,
                   COALESCE((SELECT SUM(r.amount) FROM receivables r
                             WHERE r.customer_id=c.id AND r.archived_at_utc IS NULL),0)
                       AS ReceivableAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.customer_id=c.id AND r.archived_at_utc IS NULL),0)
                       AS ReceivableOutstandingAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.customer_id=c.id AND r.archived_at_utc IS NULL
                               AND r.due_date<@BusinessDate
                               AND r.amount>COALESCE(ar.AllocatedAmount,0)),0)
                       AS ReceivableOverdueAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.customer_id=c.id AND r.archived_at_utc IS NULL
                               AND r.due_date>=@BusinessDate
                               AND r.amount>COALESCE(ar.AllocatedAmount,0)),0)
                       AS ReceivableNotDueAmount,
                   COALESCE((SELECT SUM(po.total_amount)
                             FROM purchase_orders po
                             INNER JOIN projects p ON p.id=po.project_id
                                AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                             WHERE p.customer_id=c.id AND po.archived_at_utc IS NULL
                               AND po.status NOT IN ('Draft','Cancelled')),0) AS PurchaseAmount,
                   COALESCE((SELECT SUM(ap.amount)
                             FROM payables ap
                             INNER JOIN projects p ON p.id=ap.project_id
                             WHERE p.customer_id=c.id AND ap.archived_at_utc IS NULL),0)
                       AS PayableAmount,
                   COALESCE((SELECT SUM(pa.allocated_amount)
                             FROM payment_allocations pa
                             INNER JOIN payments pm ON pm.id=pa.payment_id
                                AND pm.archived_at_utc IS NULL
                             INNER JOIN payables ap ON ap.id=pa.payable_id
                                AND ap.archived_at_utc IS NULL
                             INNER JOIN projects p ON p.id=ap.project_id
                             WHERE p.customer_id=c.id AND pa.cancelled_at_utc IS NULL),0)
                       AS PaidAllocatedAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             INNER JOIN projects p ON p.id=ap.project_id
                             WHERE p.customer_id=c.id AND ap.archived_at_utc IS NULL),0)
                       AS PayableOutstandingAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             INNER JOIN projects p ON p.id=ap.project_id
                             WHERE p.customer_id=c.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date<@BusinessDate
                               AND ap.amount>COALESCE(aa.AllocatedAmount,0)),0)
                       AS PayableOverdueAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             INNER JOIN projects p ON p.id=ap.project_id
                             WHERE p.customer_id=c.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date>=@BusinessDate
                               AND ap.amount>COALESCE(aa.AllocatedAmount,0)),0)
                       AS PayableNotDueAmount,
                   (SELECT COUNT(*) FROM shipments s
                    INNER JOIN projects p ON p.id=s.project_id
                       AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                    WHERE s.customer_id=c.id AND s.archived_at_utc IS NULL
                      AND s.status IN ('Shipped','InTransit','Received')) AS ShipmentCount,
                   (SELECT COUNT(*) FROM shipments s
                    INNER JOIN projects p ON p.id=s.project_id
                       AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                    WHERE s.customer_id=c.id AND s.archived_at_utc IS NULL
                      AND s.status='Received') AS ReceivedShipmentCount,
                   (SELECT MAX(s.shipment_date) FROM shipments s
                    INNER JOIN projects p ON p.id=s.project_id
                       AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                    WHERE s.customer_id=c.id AND s.archived_at_utc IS NULL
                      AND s.status IN ('Shipped','InTransit','Received')) AS LastShipmentDate
            FROM customers c
            WHERE c.id=@CustomerId;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CustomerAggregateRow>(
            Command(sql, new { CustomerId = customerId, BusinessDate = businessDate }, null,
                cancellationToken));
        if (row is null)
        {
            return null;
        }

        var warnings = await GetWarningsAsync(
            connection, "p.customer_id=@OwnerId", customerId, cancellationToken);
        return row.ToData(warnings);
    }

    public async Task<ProjectFinanceAggregateData?> GetProjectAsync(
        ulong projectId,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var sql =
            $$"""
            SELECT p.id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
                   c.id AS CustomerId,c.customer_code AS CustomerCode,c.name AS CustomerName,
                   p.contract_amount AS ContractAmount,
                   COALESCE((SELECT SUM(r.amount) FROM receivables r
                             WHERE r.project_id=p.id AND r.archived_at_utc IS NULL),0)
                       AS ReceivableAmount,
                   COALESCE((SELECT SUM(ra.allocated_amount)
                             FROM receipt_allocations ra
                             INNER JOIN receipts rc ON rc.id=ra.receipt_id
                                AND rc.archived_at_utc IS NULL
                             INNER JOIN receivables r ON r.id=ra.receivable_id
                                AND r.archived_at_utc IS NULL
                             WHERE r.project_id=p.id AND ra.cancelled_at_utc IS NULL),0)
                       AS ReceivedAllocatedAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.project_id=p.id AND r.archived_at_utc IS NULL),0)
                       AS ReceivableOutstandingAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.project_id=p.id AND r.archived_at_utc IS NULL
                               AND r.due_date<@BusinessDate
                               AND r.amount>COALESCE(ar.AllocatedAmount,0)),0)
                       AS ReceivableOverdueAmount,
                   COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                             FROM receivables r
                             {{ReceiptAllocationJoin}}
                             WHERE r.project_id=p.id AND r.archived_at_utc IS NULL
                               AND r.due_date>=@BusinessDate
                               AND r.amount>COALESCE(ar.AllocatedAmount,0)),0)
                       AS ReceivableNotDueAmount,
                   COALESCE((SELECT SUM(po.total_amount) FROM purchase_orders po
                             WHERE po.project_id=p.id AND po.archived_at_utc IS NULL
                               AND po.status NOT IN ('Draft','Cancelled')),0) AS PurchaseAmount,
                   (SELECT COUNT(*) FROM purchase_orders po
                    WHERE po.project_id=p.id AND po.archived_at_utc IS NULL
                      AND po.status NOT IN ('Draft','Cancelled')) AS PurchaseOrderCount,
                   (SELECT COUNT(*) FROM purchase_receipts pr
                    INNER JOIN purchase_orders po ON po.id=pr.purchase_order_id
                       AND po.archived_at_utc IS NULL
                       AND po.status NOT IN ('Draft','Cancelled')
                    WHERE po.project_id=p.id) AS PurchaseReceiptCount,
                   COALESCE((SELECT SUM(ap.amount) FROM payables ap
                             WHERE ap.project_id=p.id AND ap.archived_at_utc IS NULL),0)
                       AS PayableAmount,
                   COALESCE((SELECT SUM(pa.allocated_amount)
                             FROM payment_allocations pa
                             INNER JOIN payments pm ON pm.id=pa.payment_id
                                AND pm.archived_at_utc IS NULL
                             INNER JOIN payables ap ON ap.id=pa.payable_id
                                AND ap.archived_at_utc IS NULL
                             WHERE ap.project_id=p.id AND pa.cancelled_at_utc IS NULL),0)
                       AS PaidAllocatedAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             WHERE ap.project_id=p.id AND ap.archived_at_utc IS NULL),0)
                       AS PayableOutstandingAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             WHERE ap.project_id=p.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date<@BusinessDate
                               AND ap.amount>COALESCE(aa.AllocatedAmount,0)),0)
                       AS PayableOverdueAmount,
                   COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                             FROM payables ap
                             {{PaymentAllocationJoin}}
                             WHERE ap.project_id=p.id AND ap.archived_at_utc IS NULL
                               AND ap.due_date>=@BusinessDate
                               AND ap.amount>COALESCE(aa.AllocatedAmount,0)),0)
                       AS PayableNotDueAmount,
                   (SELECT COUNT(*) FROM shipments s
                    WHERE s.project_id=p.id AND s.archived_at_utc IS NULL
                      AND s.status IN ('Shipped','InTransit','Received')) AS ShipmentCount,
                   (SELECT COUNT(*) FROM shipments s
                    WHERE s.project_id=p.id AND s.archived_at_utc IS NULL
                      AND s.status='Received') AS ReceivedShipmentCount,
                   (SELECT COUNT(DISTINCT si.equipment_id) FROM shipment_items si
                    INNER JOIN shipments s ON s.id=si.shipment_id
                       AND s.archived_at_utc IS NULL
                       AND s.status IN ('Shipped','InTransit','Received')
                    WHERE s.project_id=p.id AND si.deleted_at_utc IS NULL
                      AND si.equipment_id IS NOT NULL) AS DeliveredEquipmentCount,
                   (SELECT MAX(s.shipment_date) FROM shipments s
                    WHERE s.project_id=p.id AND s.archived_at_utc IS NULL
                      AND s.status IN ('Shipped','InTransit','Received')) AS LastShipmentDate
            FROM projects p
            INNER JOIN customers c ON c.id=p.customer_id
            WHERE p.id=@ProjectId;

            SELECT ra.id AS AllocationId,rc.id AS ReceiptId,
                   rc.receipt_code AS ReceiptCode,rc.receipt_date AS ReceiptDate,
                   rc.amount AS ReceiptAmount,r.id AS ReceivableId,
                   r.receivable_code AS ReceivableCode,
                   ra.allocated_amount AS AllocatedAmount,
                   ra.created_at_utc AS AllocatedAtUtc
            FROM receipt_allocations ra
            INNER JOIN receipts rc ON rc.id=ra.receipt_id
               AND rc.archived_at_utc IS NULL
            INNER JOIN receivables r ON r.id=ra.receivable_id
               AND r.archived_at_utc IS NULL
            WHERE r.project_id=@ProjectId AND ra.cancelled_at_utc IS NULL
            ORDER BY rc.receipt_date DESC,ra.id DESC
            LIMIT 100;

            SELECT pa.id AS AllocationId,pm.id AS PaymentId,
                   pm.payment_code AS PaymentCode,pm.payment_date AS PaymentDate,
                   pm.amount AS PaymentAmount,ap.id AS PayableId,
                   ap.payable_code AS PayableCode,
                   pa.allocated_amount AS AllocatedAmount,
                   pa.created_at_utc AS AllocatedAtUtc
            FROM payment_allocations pa
            INNER JOIN payments pm ON pm.id=pa.payment_id
               AND pm.archived_at_utc IS NULL
            INNER JOIN payables ap ON ap.id=pa.payable_id
               AND ap.archived_at_utc IS NULL
            WHERE ap.project_id=@ProjectId AND pa.cancelled_at_utc IS NULL
            ORDER BY pm.payment_date DESC,pa.id DESC
            LIMIT 100;

            SELECT s.id AS ShipmentId,s.shipment_code AS ShipmentCode,
                   s.shipment_date AS ShipmentDate,s.status AS Status,
                   (SELECT COUNT(*) FROM shipment_items si
                    WHERE si.shipment_id=s.id AND si.deleted_at_utc IS NULL) AS ItemCount,
                   (SELECT COUNT(DISTINCT si.equipment_id) FROM shipment_items si
                    WHERE si.shipment_id=s.id AND si.deleted_at_utc IS NULL
                      AND si.equipment_id IS NOT NULL) AS EquipmentCount
            FROM shipments s
            WHERE s.project_id=@ProjectId AND s.archived_at_utc IS NULL
              AND s.status IN ('Shipped','InTransit','Received')
            ORDER BY s.shipment_date DESC,s.id DESC
            LIMIT 100;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        ProjectAggregateRow? row;
        ProjectReceiptAllocationRow[] receiptAllocations;
        ProjectPaymentAllocationRow[] paymentAllocations;
        ProjectShipmentRow[] shipments;
        using (var result = await connection.QueryMultipleAsync(
            Command(sql, new { ProjectId = projectId, BusinessDate = businessDate }, null,
                cancellationToken)))
        {
            row = await result.ReadSingleOrDefaultAsync<ProjectAggregateRow>();
            receiptAllocations = (await result.ReadAsync<ProjectReceiptAllocationRow>()).ToArray();
            paymentAllocations = (await result.ReadAsync<ProjectPaymentAllocationRow>()).ToArray();
            shipments = (await result.ReadAsync<ProjectShipmentRow>()).ToArray();
        }

        if (row is null)
        {
            return null;
        }

        var warnings = await GetWarningsAsync(
            connection, "p.id=@OwnerId", projectId, cancellationToken);
        return row.ToData(
            warnings,
            receiptAllocations.Select(item => item.ToModel()).ToArray(),
            paymentAllocations.Select(item => item.ToModel()).ToArray(),
            shipments.Select(item => item.ToModel()).ToArray());
    }

    public async Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
        CustomerProjectFinanceCriteria criteria,
        CancellationToken cancellationToken)
    {
        var sortColumn = criteria.SortBy.ToLowerInvariant() switch
        {
            "contractamount" => "ContractAmount",
            "outstandingamount" => "ReceivableOutstandingAmount",
            "overdueamount" => "ReceivableOverdueAmount",
            "purchaseamount" => "PurchaseAmount",
            "grossprofit" => "GrossProfit",
            "grossmargin" => "GrossMargin",
            _ => "UpdatedAtUtc",
        };
        var direction = criteria.SortDescending ? "DESC" : "ASC";
        var sql =
            $$"""
            SELECT p.id AS ProjectId,p.project_code AS ProjectCode,p.name AS ProjectName,
                   p.status AS ProjectStatus,p.contract_amount AS ContractAmount,
                   COALESCE(r.ReceivableAmount,0) AS ReceivableAmount,
                   COALESCE(r.ReceivedAllocatedAmount,0) AS ReceivedAllocatedAmount,
                   COALESCE(r.ReceivableOutstandingAmount,0) AS ReceivableOutstandingAmount,
                   COALESCE(r.ReceivableOverdueAmount,0) AS ReceivableOverdueAmount,
                   COALESCE(po.PurchaseAmount,0) AS PurchaseAmount,
                   COALESCE(ap.PayableAmount,0) AS PayableAmount,
                   COALESCE(ap.PaidAllocatedAmount,0) AS PaidAllocatedAmount,
                   p.contract_amount-COALESCE(po.PurchaseAmount,0) AS GrossProfit,
                   CASE WHEN p.contract_amount>0 THEN ROUND(
                       (p.contract_amount-COALESCE(po.PurchaseAmount,0)) /
                       p.contract_amount*100,2) END AS GrossMargin,
                   p.updated_at_utc AS UpdatedAtUtc
            FROM projects p
            LEFT JOIN
            (
                SELECT r.project_id,SUM(r.amount) AS ReceivableAmount,
                       SUM(COALESCE(ar.AllocatedAmount,0)) AS ReceivedAllocatedAmount,
                       SUM(r.amount-COALESCE(ar.AllocatedAmount,0)) AS ReceivableOutstandingAmount,
                       SUM(CASE WHEN r.due_date<@BusinessDate
                                THEN GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0)
                                ELSE 0 END) AS ReceivableOverdueAmount
                FROM receivables r
                {{ReceiptAllocationJoin}}
                WHERE r.archived_at_utc IS NULL
                GROUP BY r.project_id
            ) r ON r.project_id=p.id
            LEFT JOIN
            (
                SELECT project_id,SUM(total_amount) AS PurchaseAmount
                FROM purchase_orders
                WHERE archived_at_utc IS NULL AND status NOT IN ('Draft','Cancelled')
                GROUP BY project_id
            ) po ON po.project_id=p.id
            LEFT JOIN
            (
                SELECT ap.project_id,SUM(ap.amount) AS PayableAmount,
                       SUM(COALESCE(aa.AllocatedAmount,0)) AS PaidAllocatedAmount
                FROM payables ap
                {{PaymentAllocationJoin}}
                WHERE ap.archived_at_utc IS NULL
                GROUP BY ap.project_id
            ) ap ON ap.project_id=p.id
            WHERE p.customer_id=@CustomerId AND p.archived_at_utc IS NULL
              AND p.status<>'Cancelled'
            ORDER BY {{sortColumn}} {{direction}},ProjectId DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM projects p
            WHERE p.customer_id=@CustomerId AND p.archived_at_utc IS NULL
              AND p.status<>'Cancelled';
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, new
            {
                criteria.CustomerId,
                criteria.BusinessDate,
                criteria.PageSize,
                Offset = (long)(criteria.Page - 1) * criteria.PageSize,
            }, null, cancellationToken));
        var rows = (await result.ReadAsync<CustomerProjectRow>())
            .Select(row => row.ToModel())
            .ToArray();
        var total = await result.ReadSingleAsync<long>();
        return new(rows, criteria.Page, criteria.PageSize, total);
    }

    public async Task<CompanyFinanceAggregateData> GetCompanyAsync(
        FinanceCalendar calendar,
        CancellationToken cancellationToken)
    {
        var sql =
            $$"""
            SELECT
                COALESCE((SELECT SUM(r.amount) FROM receivables r
                          WHERE r.archived_at_utc IS NULL),0) AS ReceivableAmount,
                COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                          FROM receivables r {{ReceiptAllocationJoin}}
                          WHERE r.archived_at_utc IS NULL),0) AS ReceivableOutstanding,
                COALESCE((SELECT SUM(r.amount-COALESCE(ar.AllocatedAmount,0))
                          FROM receivables r {{ReceiptAllocationJoin}}
                          WHERE r.archived_at_utc IS NULL AND r.due_date<@BusinessDate
                            AND r.amount>COALESCE(ar.AllocatedAmount,0)),0)
                    AS ReceivableOverdue,
                COALESCE((SELECT SUM(amount) FROM receipts WHERE archived_at_utc IS NULL
                          AND receipt_date>=@MonthStart AND receipt_date<@NextMonthStart),0)
                    AS CashReceivedThisMonth,
                COALESCE((SELECT SUM(amount) FROM receipts WHERE archived_at_utc IS NULL
                          AND receipt_date>=@YearStart AND receipt_date<@NextYearStart),0)
                    AS CashReceivedYearToDate,
                COALESCE((SELECT SUM(ra.allocated_amount) FROM receipt_allocations ra
                          INNER JOIN receipts rc ON rc.id=ra.receipt_id
                             AND rc.archived_at_utc IS NULL
                          INNER JOIN receivables r ON r.id=ra.receivable_id
                             AND r.archived_at_utc IS NULL
                          WHERE ra.cancelled_at_utc IS NULL
                            AND rc.receipt_date>=@MonthStart
                            AND rc.receipt_date<@NextMonthStart),0) AS ReceivedAllocatedThisMonth,
                COALESCE((SELECT SUM(ra.allocated_amount) FROM receipt_allocations ra
                          INNER JOIN receipts rc ON rc.id=ra.receipt_id
                             AND rc.archived_at_utc IS NULL
                          INNER JOIN receivables r ON r.id=ra.receivable_id
                             AND r.archived_at_utc IS NULL
                          WHERE ra.cancelled_at_utc IS NULL
                            AND rc.receipt_date>=@YearStart
                            AND rc.receipt_date<@NextYearStart),0) AS ReceivedAllocatedYearToDate,
                COALESCE((SELECT SUM(po.total_amount) FROM purchase_orders po
                          INNER JOIN projects p ON p.id=po.project_id
                             AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                          WHERE po.archived_at_utc IS NULL
                            AND po.status NOT IN ('Draft','Cancelled')
                            AND po.order_date>=@MonthStart AND po.order_date<@NextMonthStart),0)
                    AS PurchaseThisMonth,
                COALESCE((SELECT SUM(po.total_amount) FROM purchase_orders po
                          INNER JOIN projects p ON p.id=po.project_id
                             AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                          WHERE po.archived_at_utc IS NULL
                            AND po.status NOT IN ('Draft','Cancelled')
                            AND po.order_date>=@YearStart AND po.order_date<@NextYearStart),0)
                    AS PurchaseYearToDate,
                COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                          FROM payables ap {{PaymentAllocationJoin}}
                          WHERE ap.archived_at_utc IS NULL),0) AS PayableOutstanding,
                COALESCE((SELECT SUM(ap.amount-COALESCE(aa.AllocatedAmount,0))
                          FROM payables ap {{PaymentAllocationJoin}}
                          WHERE ap.archived_at_utc IS NULL AND ap.due_date<@BusinessDate
                            AND ap.amount>COALESCE(aa.AllocatedAmount,0)),0) AS PayableOverdue,
                COALESCE((SELECT SUM(amount) FROM payments WHERE archived_at_utc IS NULL
                          AND payment_date>=@MonthStart AND payment_date<@NextMonthStart),0)
                    AS CashPaidThisMonth,
                COALESCE((SELECT SUM(amount) FROM payments WHERE archived_at_utc IS NULL
                          AND payment_date>=@YearStart AND payment_date<@NextYearStart),0)
                    AS CashPaidYearToDate,
                COALESCE((SELECT SUM(pa.allocated_amount) FROM payment_allocations pa
                          INNER JOIN payments pm ON pm.id=pa.payment_id
                             AND pm.archived_at_utc IS NULL
                          INNER JOIN payables ap ON ap.id=pa.payable_id
                             AND ap.archived_at_utc IS NULL
                          WHERE pa.cancelled_at_utc IS NULL
                            AND pm.payment_date>=@MonthStart
                            AND pm.payment_date<@NextMonthStart),0) AS PaidAllocatedThisMonth,
                COALESCE((SELECT SUM(pa.allocated_amount) FROM payment_allocations pa
                          INNER JOIN payments pm ON pm.id=pa.payment_id
                             AND pm.archived_at_utc IS NULL
                          INNER JOIN payables ap ON ap.id=pa.payable_id
                             AND ap.archived_at_utc IS NULL
                          WHERE pa.cancelled_at_utc IS NULL
                            AND pm.payment_date>=@YearStart
                            AND pm.payment_date<@NextYearStart),0) AS PaidAllocatedYearToDate,
                COALESCE((SELECT SUM(contract_amount) FROM projects
                          WHERE archived_at_utc IS NULL
                            AND status IN ('Planning','Active','OnHold')),0)
                    AS ActiveProjectContractAmount,
                COALESCE((SELECT SUM(contract_amount) FROM projects
                          WHERE archived_at_utc IS NULL AND status<>'Cancelled'),0)
                    AS EligibleProjectContractAmount,
                COALESCE((SELECT SUM(po.total_amount) FROM purchase_orders po
                          INNER JOIN projects p ON p.id=po.project_id
                             AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                          WHERE po.archived_at_utc IS NULL
                            AND po.status NOT IN ('Draft','Cancelled')),0)
                    AS EligibleProjectPurchaseAmount,
                (SELECT COUNT(*) FROM shipments s
                 INNER JOIN projects p ON p.id=s.project_id
                    AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                 WHERE s.archived_at_utc IS NULL
                   AND s.status IN ('Shipped','InTransit','Received')
                   AND s.shipment_date>=@MonthStart AND s.shipment_date<@NextMonthStart)
                    AS ShipmentThisMonth;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<CompanyAggregateRow>(
            Command(sql, calendar, null, cancellationToken));
        var warnings = await GetWarningsAsync(connection, "1=1", 0, cancellationToken);
        return row.ToData(warnings);
    }

    public Task<FinanceAgingOverview> GetReceivableAgingAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken) =>
        GetAgingAsync(true, businessDate, cancellationToken);

    public Task<FinanceAgingOverview> GetPayableAgingAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken) =>
        GetAgingAsync(false, businessDate, cancellationToken);

    public async Task<FinanceDashboardAggregateData> GetDashboardAsync(
        FinanceDashboardCriteria criteria,
        CancellationToken cancellationToken)
    {
        var receivableBalances =
            $$"""
              SELECT r.id AS ReceivableId,r.receivable_code AS ReceivableCode,
                     r.customer_id AS CustomerId,c.name AS CustomerName,
                     r.project_id AS ProjectId,p.project_code AS ProjectCode,
                     p.name AS ProjectName,r.due_date AS DueDate,
                     DATEDIFF(@BusinessDate,r.due_date) AS OverdueDays,
                     GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0)
                         AS RemainingAmount
              FROM receivables r
              INNER JOIN customers c ON c.id=r.customer_id
              INNER JOIN projects p ON p.id=r.project_id
              {{ReceiptAllocationJoin}}
              WHERE r.archived_at_utc IS NULL
                AND r.due_date<@BusinessDate
                AND r.amount>COALESCE(ar.AllocatedAmount,0)
              """;
        var payableBalances =
            $$"""
              SELECT ap.id AS PayableId,ap.payable_code AS PayableCode,
                     ap.supplier_id AS SupplierId,s.name AS SupplierName,
                     ap.project_id AS ProjectId,p.project_code AS ProjectCode,
                     p.name AS ProjectName,ap.due_date AS DueDate,
                     DATEDIFF(@BusinessDate,ap.due_date) AS OverdueDays,
                     GREATEST(ap.amount-COALESCE(aa.AllocatedAmount,0),0)
                         AS RemainingAmount
              FROM payables ap
              INNER JOIN suppliers s ON s.id=ap.supplier_id
              INNER JOIN projects p ON p.id=ap.project_id
              {{PaymentAllocationJoin}}
              WHERE ap.archived_at_utc IS NULL
                AND ap.due_date<@BusinessDate
                AND ap.amount>COALESCE(aa.AllocatedAmount,0)
              """;
        var projectRankingSql = BuildProjectRankingSql("ContractAmount");
        var sql =
            $$"""
            SELECT Month,SUM(ReceivedAmount) AS ReceivedAmount,
                   SUM(PaidAmount) AS PaidAmount
            FROM
            (
                SELECT DATE_FORMAT(receipt_date,'%Y-%m') AS Month,
                       SUM(amount) AS ReceivedAmount,0 AS PaidAmount
                FROM receipts
                WHERE archived_at_utc IS NULL
                  AND receipt_date>=@TrendStart AND receipt_date<@TrendEnd
                GROUP BY DATE_FORMAT(receipt_date,'%Y-%m')
                UNION ALL
                SELECT DATE_FORMAT(payment_date,'%Y-%m') AS Month,
                       0 AS ReceivedAmount,SUM(amount) AS PaidAmount
                FROM payments
                WHERE archived_at_utc IS NULL
                  AND payment_date>=@TrendStart AND payment_date<@TrendEnd
                GROUP BY DATE_FORMAT(payment_date,'%Y-%m')
            ) cash_flow
            GROUP BY Month
            ORDER BY Month;

            SELECT
                COALESCE(SUM(CASE WHEN Kind='AR' THEN 1 ELSE 0 END),0)
                    AS OverdueReceivableCount,
                COALESCE(SUM(CASE WHEN Kind='AR' THEN Amount ELSE 0 END),0)
                    AS OverdueReceivableAmount,
                COALESCE(SUM(CASE WHEN Kind='AP' THEN 1 ELSE 0 END),0)
                    AS OverduePayableCount,
                COALESCE(SUM(CASE WHEN Kind='AP' THEN Amount ELSE 0 END),0)
                    AS OverduePayableAmount,
                COALESCE(SUM(CASE WHEN Kind='NEG' THEN 1 ELSE 0 END),0)
                    AS NegativeGrossProjectCount,
                COALESCE(SUM(CASE WHEN Kind='NEG' THEN Amount ELSE 0 END),0)
                    AS NegativeGrossProfitAmount
            FROM
            (
                SELECT 'AR' AS Kind,RemainingAmount AS Amount
                FROM ({{receivableBalances}}) overdue_receivables
                UNION ALL
                SELECT 'AP' AS Kind,RemainingAmount AS Amount
                FROM ({{payableBalances}}) overdue_payables
                UNION ALL
                SELECT 'NEG' AS Kind,GrossProfit AS Amount
                FROM ({{ProjectMetricsSource}}) project_metrics
                WHERE GrossProfit<0
            ) risk_items;

            SELECT * FROM ({{receivableBalances}}) overdue_receivables
            ORDER BY RemainingAmount DESC,OverdueDays DESC,ReceivableId DESC
            LIMIT @Limit;

            SELECT * FROM ({{payableBalances}}) overdue_payables
            ORDER BY RemainingAmount DESC,OverdueDays DESC,PayableId DESC
            LIMIT @Limit;

            {{projectRankingSql}}

            SELECT c.id AS CustomerId,c.customer_code AS CustomerCode,
                   c.name AS CustomerName,
                   COALESCE(project_totals.ContractAmount,0) AS ContractAmount,
                   totals.ReceivableAmount,totals.OutstandingAmount,totals.OverdueAmount,
                   COALESCE(receipt_totals.CashReceivedAmount,0) AS CashReceivedAmount,
                   COALESCE(receipt_totals.UnallocatedReceiptAmount,0)
                       AS UnallocatedReceiptAmount
            FROM customers c
            INNER JOIN
            (
                SELECT r.customer_id,SUM(r.amount) AS ReceivableAmount,
                       SUM(GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0))
                           AS OutstandingAmount,
                       SUM(CASE WHEN r.due_date<@BusinessDate
                                THEN GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0)
                                ELSE 0 END) AS OverdueAmount
                FROM receivables r
                {{ReceiptAllocationJoin}}
                WHERE r.archived_at_utc IS NULL
                GROUP BY r.customer_id
            ) totals ON totals.customer_id=c.id
            LEFT JOIN
            (
                SELECT p.customer_id,SUM(p.contract_amount) AS ContractAmount
                FROM projects p
                WHERE p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                GROUP BY p.customer_id
            ) project_totals ON project_totals.customer_id=c.id
            LEFT JOIN
            (
                SELECT rc.customer_id,SUM(rc.amount) AS CashReceivedAmount,
                       SUM(rc.amount-COALESCE(allocated.AllocatedAmount,0))
                           AS UnallocatedReceiptAmount
                FROM receipts rc
                LEFT JOIN
                (
                    SELECT ra.receipt_id,SUM(ra.allocated_amount) AS AllocatedAmount
                    FROM receipt_allocations ra
                    INNER JOIN receivables r ON r.id=ra.receivable_id
                       AND r.archived_at_utc IS NULL
                    WHERE ra.cancelled_at_utc IS NULL
                    GROUP BY ra.receipt_id
                ) allocated ON allocated.receipt_id=rc.id
                WHERE rc.archived_at_utc IS NULL
                GROUP BY rc.customer_id
            ) receipt_totals ON receipt_totals.customer_id=c.id
            WHERE totals.OutstandingAmount>0
            ORDER BY totals.OutstandingAmount DESC,c.id DESC
            LIMIT @Limit;

            SELECT s.id AS SupplierId,s.supplier_code AS SupplierCode,
                   s.name AS SupplierName,COALESCE(po.PurchaseAmount,0) AS PurchaseAmount,
                   totals.PayableAmount,totals.OutstandingAmount,totals.OverdueAmount,
                   COALESCE(pm.CashPaidAmount,0) AS CashPaidAmount,
                   COALESCE(pm.AllocatedPaidAmount,0) AS AllocatedPaidAmount,
                   COALESCE(pm.CashPaidAmount-pm.AllocatedPaidAmount,0)
                       AS UnallocatedPaymentAmount
            FROM suppliers s
            INNER JOIN
            (
                SELECT ap.supplier_id,SUM(ap.amount) AS PayableAmount,
                       SUM(GREATEST(ap.amount-COALESCE(aa.AllocatedAmount,0),0))
                           AS OutstandingAmount,
                       SUM(CASE WHEN ap.due_date<@BusinessDate
                                THEN GREATEST(ap.amount-COALESCE(aa.AllocatedAmount,0),0)
                                ELSE 0 END) AS OverdueAmount
                FROM payables ap
                {{PaymentAllocationJoin}}
                WHERE ap.archived_at_utc IS NULL
                GROUP BY ap.supplier_id
            ) totals ON totals.supplier_id=s.id
            LEFT JOIN
            (
                SELECT po.supplier_id,SUM(po.total_amount) AS PurchaseAmount
                FROM purchase_orders po
                INNER JOIN projects p ON p.id=po.project_id
                   AND p.archived_at_utc IS NULL AND p.status<>'Cancelled'
                WHERE po.archived_at_utc IS NULL
                  AND po.status IN ('Ordered','PartiallyReceived','Received')
                GROUP BY po.supplier_id
            ) po ON po.supplier_id=s.id
            LEFT JOIN
            (
                SELECT pm.supplier_id,SUM(pm.amount) AS CashPaidAmount,
                       SUM(COALESCE(pa.AllocatedAmount,0)) AS AllocatedPaidAmount
                FROM payments pm
                LEFT JOIN
                (
                    SELECT pa.payment_id,SUM(pa.allocated_amount) AS AllocatedAmount
                    FROM payment_allocations pa
                    INNER JOIN payables ap ON ap.id=pa.payable_id
                       AND ap.archived_at_utc IS NULL
                    WHERE pa.cancelled_at_utc IS NULL
                    GROUP BY pa.payment_id
                ) pa ON pa.payment_id=pm.id
                WHERE pm.archived_at_utc IS NULL
                GROUP BY pm.supplier_id
            ) pm ON pm.supplier_id=s.id
            WHERE s.archived_at_utc IS NULL AND totals.OutstandingAmount>0
            ORDER BY totals.OutstandingAmount DESC,s.id DESC
            LIMIT @Limit;
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(
            Command(sql, criteria, null, cancellationToken));
        var cashFlow = (await result.ReadAsync<CashFlowRow>())
            .Select(row => row.ToModel()).ToArray();
        var risk = (await result.ReadSingleAsync<RiskAggregateRow>()).ToModel();
        var overdueReceivables = (await result.ReadAsync<OverdueReceivableRow>())
            .Select(row => row.ToModel()).ToArray();
        var overduePayables = (await result.ReadAsync<OverduePayableRow>())
            .Select(row => row.ToModel()).ToArray();
        var projectRanking = (await result.ReadAsync<ProjectRankingRow>())
            .Select(row => row.ToModel()).ToArray();
        var customerRanking = (await result.ReadAsync<CustomerRankingRow>())
            .Select(row => row.ToModel()).ToArray();
        var supplierRanking = (await result.ReadAsync<SupplierRankingRow>())
            .Select(row => row.ToModel()).ToArray();
        return new FinanceDashboardAggregateData(
            cashFlow, risk, overdueReceivables, overduePayables, projectRanking,
            customerRanking, supplierRanking);
    }

    public async Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
        FinanceProjectRankingCriteria criteria,
        CancellationToken cancellationToken)
    {
        var sortColumn = criteria.SortBy.ToLowerInvariant() switch
        {
            "grossprofit" => "GrossProfit",
            "grossmargin" => "GrossMargin",
            "outstandingamount" => "ReceivableOutstandingAmount",
            "overdueamount" => "ReceivableOverdueAmount",
            _ => "ContractAmount",
        };
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProjectRankingRow>(
            Command(BuildProjectRankingSql(sortColumn), criteria, null, cancellationToken));
        return rows.Select(row => row.ToModel()).ToArray();
    }

    public async Task CreateReceivablePlanAsync(
        ulong projectId,
        ReceivablePlanWriteData data,
        CancellationToken cancellationToken)
    {
        const string projectSql =
            """
            SELECT p.customer_id AS CustomerId,p.project_code AS ProjectCode,
                   p.name AS ProjectName,p.contract_amount AS ContractAmount,
                   COALESCE((SELECT SUM(r.amount) FROM receivables r
                             WHERE r.project_id=p.id AND r.archived_at_utc IS NULL),0)
                       AS PlannedAmount
            FROM projects p
            WHERE p.id=@ProjectId AND p.archived_at_utc IS NULL
            FOR UPDATE;
            """;
        const string sequenceSql =
            """
            INSERT INTO number_sequences
                (sequence_name,current_value,version,updated_at_utc)
            VALUES (@SequenceName,LAST_INSERT_ID(1),1,@NowUtc)
            ON DUPLICATE KEY UPDATE
                current_value=LAST_INSERT_ID(current_value+1),
                version=version+1,updated_at_utc=VALUES(updated_at_utc);
            SELECT LAST_INSERT_ID();
            """;
        const string insertSql =
            """
            INSERT INTO receivables
                (receivable_code,customer_id,project_id,title,receivable_type,
                 amount,due_date,remark,version,created_at_utc,created_by_user_id,
                 updated_at_utc,updated_by_user_id)
            VALUES
                (@Code,@CustomerId,@ProjectId,@Title,@ReceivableType,@Amount,
                 @DueDate,@Remark,1,@NowUtc,@ActorUserId,@NowUtc,@ActorUserId);
            SELECT LAST_INSERT_ID();
            """;
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var project = await connection.QuerySingleOrDefaultAsync<ProjectPlanRow>(
            Command(projectSql, new { ProjectId = projectId }, transaction, cancellationToken));
        if (project is null)
        {
            throw new NotFoundException("项目不存在或已归档。");
        }

        var batchAmount = data.Items.Sum(item => item.Amount);
        if (project.ContractAmount <= 0)
        {
            throw FlowHearthValidationException.For(
                "items", "项目合同金额为 0，请先完善合同金额后再创建应收计划。");
        }

        if (project.PlannedAmount + batchAmount > project.ContractAmount)
        {
            throw FlowHearthValidationException.For("items", "应收计划总额不能超过项目合同金额。");
        }

        var created = new List<object>(data.Items.Count);
        foreach (var item in data.Items)
        {
            var sequence = await connection.QuerySingleAsync<ulong>(
                Command(sequenceSql, new
                {
                    SequenceName = $"receivable:{data.NowUtc.Year:D4}",
                    data.NowUtc,
                }, transaction, cancellationToken));
            var code = FinanceCodes.Format(
                runtime.NumberPrefixes.Receivable, data.NowUtc.Year, sequence);
            var id = await connection.QuerySingleAsync<ulong>(
                Command(insertSql, new
                {
                    Code = code,
                    project.CustomerId,
                    ProjectId = projectId,
                    item.Title,
                    ReceivableType = item.ReceivableType.ToString(),
                    item.Amount,
                    item.DueDate,
                    item.Remark,
                    data.NowUtc,
                    data.ActorUserId,
                }, transaction, cancellationToken));
            var after = new
            {
                code,
                projectId,
                project.CustomerId,
                item.Title,
                receivableType = item.ReceivableType.ToString(),
                item.Amount,
                item.DueDate,
                item.Remark,
            };
            await WriteAuditAsync(
                connection, transaction, data.ActorUserId, "receivable.created", id,
                code, $"批量创建应收 {item.Title}", after, data.NowUtc, cancellationToken);
            created.Add(after);
        }

        await WriteAuditAsync(
            connection, transaction, data.ActorUserId, "receivable.plan.created",
            projectId, project.ProjectCode, $"创建 {data.Items.Count} 阶段项目应收计划",
            new { projectId, batchAmount, items = created }, data.NowUtc, cancellationToken,
            "project");
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<FinanceAgingOverview> GetAgingAsync(
        bool receivable,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var source = receivable
            ? $$"""
              SELECT r.id,r.due_date,
                     GREATEST(r.amount-COALESCE(ar.AllocatedAmount,0),0) RemainingAmount
              FROM receivables r
              {{ReceiptAllocationJoin}}
              WHERE r.archived_at_utc IS NULL
                AND r.amount>COALESCE(ar.AllocatedAmount,0)
              """
            : $$"""
              SELECT ap.id,ap.due_date,
                     GREATEST(ap.amount-COALESCE(aa.AllocatedAmount,0),0) RemainingAmount
              FROM payables ap
              {{PaymentAllocationJoin}}
              WHERE ap.archived_at_utc IS NULL
                AND ap.amount>COALESCE(aa.AllocatedAmount,0)
              """;
        var sql =
            $$"""
            SELECT CASE
                     WHEN due_date>=@BusinessDate THEN 'NotDue'
                     WHEN DATEDIFF(@BusinessDate,due_date)<=30 THEN '1-30'
                     WHEN DATEDIFF(@BusinessDate,due_date)<=60 THEN '31-60'
                     WHEN DATEDIFF(@BusinessDate,due_date)<=90 THEN '61-90'
                     ELSE '90+'
                   END AS Bucket,
                   SUM(RemainingAmount) AS Amount,COUNT(*) AS ItemCount
            FROM ({{source}}) balances
            GROUP BY Bucket;
            """;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<AgingRow>(
            Command(sql, new { BusinessDate = businessDate }, null, cancellationToken)))
            .ToDictionary(row => row.Bucket, StringComparer.Ordinal);
        var buckets = AgingBucketNames
            .Select(name => rows.TryGetValue(name, out var row)
                ? new FinanceAgingBucket(name, row.Amount, row.ItemCount)
                : new FinanceAgingBucket(name, 0, 0))
            .ToArray();
        return new FinanceAgingOverview(
            businessDate, buckets.Sum(item => item.Amount), buckets);
    }

    private static async Task<IReadOnlyList<FinanceConsistencyWarning>> GetWarningsAsync(
        DbConnection connection,
        string projectScope,
        ulong ownerId,
        CancellationToken cancellationToken)
    {
        var sql =
            $$"""
            SELECT 'receivable_plan_exceeds_contract' AS Code,
                   '应收计划总额超过项目合同金额。' AS Message,COUNT(*) AS Count
            FROM projects p
            WHERE {{projectScope}}
              AND p.contract_amount>=0
              AND (SELECT COALESCE(SUM(r.amount),0) FROM receivables r
                   WHERE r.project_id=p.id AND r.archived_at_utc IS NULL)>p.contract_amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'payable_plan_exceeds_purchase','采购应付计划超过采购单金额。',COUNT(*)
            FROM purchase_orders po
            INNER JOIN projects p ON p.id=po.project_id
            WHERE {{projectScope}} AND po.archived_at_utc IS NULL
              AND (SELECT COALESCE(SUM(ap.amount),0) FROM payables ap
                   WHERE ap.purchase_order_id=po.id AND ap.archived_at_utc IS NULL)>po.total_amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'receivable_over_allocated','应收核销金额超过应收金额。',COUNT(*)
            FROM receivables r
            INNER JOIN projects p ON p.id=r.project_id
            WHERE {{projectScope}} AND r.archived_at_utc IS NULL
              AND (SELECT COALESCE(SUM(ra.allocated_amount),0)
                   FROM receipt_allocations ra
                   INNER JOIN receipts rc ON rc.id=ra.receipt_id
                      AND rc.archived_at_utc IS NULL
                   WHERE ra.receivable_id=r.id AND ra.cancelled_at_utc IS NULL)>r.amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'payable_over_allocated','应付核销金额超过应付金额。',COUNT(*)
            FROM payables ap
            INNER JOIN projects p ON p.id=ap.project_id
            WHERE {{projectScope}} AND ap.archived_at_utc IS NULL
              AND (SELECT COALESCE(SUM(pa.allocated_amount),0)
                   FROM payment_allocations pa
                   INNER JOIN payments pm ON pm.id=pa.payment_id
                      AND pm.archived_at_utc IS NULL
                   WHERE pa.payable_id=ap.id AND pa.cancelled_at_utc IS NULL)>ap.amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'receipt_over_allocated','收款核销总额超过收款金额。',COUNT(*)
            FROM receipts rc
            WHERE rc.archived_at_utc IS NULL
              AND EXISTS
              (
                  SELECT 1 FROM receipt_allocations scope_ra
                  INNER JOIN receivables r ON r.id=scope_ra.receivable_id
                  INNER JOIN projects p ON p.id=r.project_id
                  WHERE scope_ra.receipt_id=rc.id
                    AND scope_ra.cancelled_at_utc IS NULL AND {{projectScope}}
              )
              AND (SELECT COALESCE(SUM(ra.allocated_amount),0)
                   FROM receipt_allocations ra
                   INNER JOIN receivables r ON r.id=ra.receivable_id
                      AND r.archived_at_utc IS NULL
                   WHERE ra.receipt_id=rc.id AND ra.cancelled_at_utc IS NULL)>rc.amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'payment_over_allocated','付款核销总额超过付款金额。',COUNT(*)
            FROM payments pm
            WHERE pm.archived_at_utc IS NULL
              AND EXISTS
              (
                  SELECT 1 FROM payment_allocations scope_pa
                  INNER JOIN payables ap ON ap.id=scope_pa.payable_id
                  INNER JOIN projects p ON p.id=ap.project_id
                  WHERE scope_pa.payment_id=pm.id
                    AND scope_pa.cancelled_at_utc IS NULL AND {{projectScope}}
              )
              AND (SELECT COALESCE(SUM(pa.allocated_amount),0)
                   FROM payment_allocations pa
                   INNER JOIN payables ap ON ap.id=pa.payable_id
                      AND ap.archived_at_utc IS NULL
                   WHERE pa.payment_id=pm.id AND pa.cancelled_at_utc IS NULL)>pm.amount
            HAVING COUNT(*)>0
            UNION ALL
            SELECT 'duplicate_formal_equipment_shipment','同一设备存在重复正式出货记录。',COUNT(*)
            FROM
            (
                SELECT si.equipment_id
                FROM shipment_items si
                INNER JOIN shipments s ON s.id=si.shipment_id
                   AND s.archived_at_utc IS NULL
                   AND s.status IN ('Shipped','InTransit','Received')
                INNER JOIN projects p ON p.id=s.project_id
                WHERE {{projectScope}} AND si.deleted_at_utc IS NULL
                  AND si.equipment_id IS NOT NULL
                GROUP BY si.equipment_id HAVING COUNT(*)>1
            ) duplicates
            HAVING COUNT(*)>0;
            """;
        var rows = await connection.QueryAsync<WarningRow>(
            Command(sql, new { OwnerId = ownerId }, null, cancellationToken));
        return rows.Select(row => new FinanceConsistencyWarning(
            row.Code, row.Message, row.Count)).ToArray();
    }

    private static Task<int> WriteAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong actorUserId,
        string action,
        ulong entityId,
        string entityCode,
        string summary,
        object after,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        string entityType = "receivable") =>
        connection.ExecuteAsync(Command(
            """
            INSERT INTO audit_logs
                (occurred_at_utc,actor_user_id,action,entity_type,entity_id,
                 entity_code,summary,after_json)
            VALUES
                (@NowUtc,@ActorUserId,@Action,@EntityType,@EntityId,
                 @EntityCode,@Summary,@AfterJson);
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
                AfterJson = JsonSerializer.Serialize(after),
            }, transaction, cancellationToken));

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    private static string BuildProjectRankingSql(string sortColumn) =>
        $$"""
        SELECT ProjectId,ProjectCode,ProjectName,CustomerId,CustomerName,ProjectStatus,
               ContractAmount,PurchaseAmount,GrossProfit,GrossMargin,
               ReceivableOutstandingAmount,ReceivableOverdueAmount
        FROM ({{ProjectMetricsSource}}) project_metrics
        ORDER BY {{sortColumn}} DESC,ProjectId DESC
        LIMIT @Limit;
        """;

    private sealed class CustomerAggregateRow
    {
        public ulong CustomerId { get; init; }
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public int ProjectCount { get; init; }
        public int ActiveProjectCount { get; init; }
        public decimal ContractAmount { get; init; }
        public decimal ReceiptAmount { get; init; }
        public decimal ReceivedAllocatedAmount { get; init; }
        public decimal UnallocatedReceiptAmount { get; init; }
        public decimal ReceivableAmount { get; init; }
        public decimal ReceivableOutstandingAmount { get; init; }
        public decimal ReceivableOverdueAmount { get; init; }
        public decimal ReceivableNotDueAmount { get; init; }
        public decimal PurchaseAmount { get; init; }
        public decimal PayableAmount { get; init; }
        public decimal PaidAllocatedAmount { get; init; }
        public decimal PayableOutstandingAmount { get; init; }
        public decimal PayableOverdueAmount { get; init; }
        public decimal PayableNotDueAmount { get; init; }
        public int ShipmentCount { get; init; }
        public int ReceivedShipmentCount { get; init; }
        public DateOnly? LastShipmentDate { get; init; }

        public CustomerFinanceAggregateData ToData(
            IReadOnlyList<FinanceConsistencyWarning> warnings) =>
            new(CustomerId, CustomerCode, CustomerName, ProjectCount, ActiveProjectCount,
                ContractAmount, ReceiptAmount, ReceivedAllocatedAmount,
                UnallocatedReceiptAmount, ReceivableAmount,
                ReceivableOutstandingAmount, ReceivableOverdueAmount,
                ReceivableNotDueAmount, PurchaseAmount, PayableAmount,
                PaidAllocatedAmount, PayableOutstandingAmount, PayableOverdueAmount,
                PayableNotDueAmount, ShipmentCount, ReceivedShipmentCount,
                LastShipmentDate, warnings);
    }

    private sealed class ProjectAggregateRow
    {
        public ulong ProjectId { get; init; }
        public ulong CustomerId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public decimal ContractAmount { get; init; }
        public decimal ReceivableAmount { get; init; }
        public decimal ReceivedAllocatedAmount { get; init; }
        public decimal ReceivableOutstandingAmount { get; init; }
        public decimal ReceivableOverdueAmount { get; init; }
        public decimal ReceivableNotDueAmount { get; init; }
        public decimal PurchaseAmount { get; init; }
        public int PurchaseOrderCount { get; init; }
        public int PurchaseReceiptCount { get; init; }
        public decimal PayableAmount { get; init; }
        public decimal PaidAllocatedAmount { get; init; }
        public decimal PayableOutstandingAmount { get; init; }
        public decimal PayableOverdueAmount { get; init; }
        public decimal PayableNotDueAmount { get; init; }
        public int ShipmentCount { get; init; }
        public int ReceivedShipmentCount { get; init; }
        public int DeliveredEquipmentCount { get; init; }
        public DateOnly? LastShipmentDate { get; init; }

        public ProjectFinanceAggregateData ToData(
            IReadOnlyList<FinanceConsistencyWarning> warnings,
            IReadOnlyList<ProjectReceiptAllocationSummary> receiptAllocations,
            IReadOnlyList<ProjectPaymentAllocationSummary> paymentAllocations,
            IReadOnlyList<ProjectShipmentSummary> shipments) =>
            new(ProjectId, CustomerId, ProjectCode, ProjectName, CustomerCode,
                CustomerName, ContractAmount, ReceivableAmount,
                ReceivedAllocatedAmount, ReceivableOutstandingAmount,
                ReceivableOverdueAmount, ReceivableNotDueAmount, PurchaseAmount,
                PurchaseOrderCount, PurchaseReceiptCount, PayableAmount,
                PaidAllocatedAmount, PayableOutstandingAmount,
                PayableOverdueAmount, PayableNotDueAmount, ShipmentCount,
                ReceivedShipmentCount, DeliveredEquipmentCount, LastShipmentDate,
                warnings, receiptAllocations, paymentAllocations, shipments);
    }

    private sealed class ProjectReceiptAllocationRow
    {
        public ulong AllocationId { get; init; }
        public ulong ReceiptId { get; init; }
        public string ReceiptCode { get; init; } = string.Empty;
        public DateOnly ReceiptDate { get; init; }
        public decimal ReceiptAmount { get; init; }
        public ulong ReceivableId { get; init; }
        public string ReceivableCode { get; init; } = string.Empty;
        public decimal AllocatedAmount { get; init; }
        public DateTime AllocatedAtUtc { get; init; }

        public ProjectReceiptAllocationSummary ToModel() =>
            new(AllocationId, ReceiptId, ReceiptCode, ReceiptDate, ReceiptAmount,
                ReceivableId, ReceivableCode, AllocatedAmount, AllocatedAtUtc);
    }

    private sealed class ProjectPaymentAllocationRow
    {
        public ulong AllocationId { get; init; }
        public ulong PaymentId { get; init; }
        public string PaymentCode { get; init; } = string.Empty;
        public DateOnly PaymentDate { get; init; }
        public decimal PaymentAmount { get; init; }
        public ulong PayableId { get; init; }
        public string PayableCode { get; init; } = string.Empty;
        public decimal AllocatedAmount { get; init; }
        public DateTime AllocatedAtUtc { get; init; }

        public ProjectPaymentAllocationSummary ToModel() =>
            new(AllocationId, PaymentId, PaymentCode, PaymentDate, PaymentAmount,
                PayableId, PayableCode, AllocatedAmount, AllocatedAtUtc);
    }

    private sealed class ProjectShipmentRow
    {
        public ulong ShipmentId { get; init; }
        public string ShipmentCode { get; init; } = string.Empty;
        public DateOnly ShipmentDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public int ItemCount { get; init; }
        public int EquipmentCount { get; init; }

        public ProjectShipmentSummary ToModel() =>
            new(ShipmentId, ShipmentCode, ShipmentDate,
                Enum.Parse<ShipmentStatus>(Status), ItemCount, EquipmentCount);
    }

    private sealed class CustomerProjectRow
    {
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public string ProjectStatus { get; init; } = string.Empty;
        public decimal ContractAmount { get; init; }
        public decimal ReceivableAmount { get; init; }
        public decimal ReceivedAllocatedAmount { get; init; }
        public decimal ReceivableOutstandingAmount { get; init; }
        public decimal ReceivableOverdueAmount { get; init; }
        public decimal PurchaseAmount { get; init; }
        public decimal PayableAmount { get; init; }
        public decimal PaidAllocatedAmount { get; init; }
        public decimal GrossProfit { get; init; }
        public decimal? GrossMargin { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public CustomerProjectFinanceRow ToModel() =>
            new(ProjectId, ProjectCode, ProjectName,
                Enum.Parse<ProjectStatus>(ProjectStatus), ContractAmount,
                ReceivableAmount, ReceivedAllocatedAmount,
                ReceivableOutstandingAmount, ReceivableOverdueAmount,
                PurchaseAmount, PayableAmount, PaidAllocatedAmount,
                GrossProfit, GrossMargin, UpdatedAtUtc);
    }

    private sealed class CompanyAggregateRow
    {
        public decimal ReceivableAmount { get; init; }
        public decimal ReceivableOutstanding { get; init; }
        public decimal ReceivableOverdue { get; init; }
        public decimal CashReceivedThisMonth { get; init; }
        public decimal CashReceivedYearToDate { get; init; }
        public decimal ReceivedAllocatedThisMonth { get; init; }
        public decimal ReceivedAllocatedYearToDate { get; init; }
        public decimal PurchaseThisMonth { get; init; }
        public decimal PurchaseYearToDate { get; init; }
        public decimal PayableOutstanding { get; init; }
        public decimal PayableOverdue { get; init; }
        public decimal CashPaidThisMonth { get; init; }
        public decimal CashPaidYearToDate { get; init; }
        public decimal PaidAllocatedThisMonth { get; init; }
        public decimal PaidAllocatedYearToDate { get; init; }
        public decimal ActiveProjectContractAmount { get; init; }
        public decimal EligibleProjectContractAmount { get; init; }
        public decimal EligibleProjectPurchaseAmount { get; init; }
        public int ShipmentThisMonth { get; init; }

        public CompanyFinanceAggregateData ToData(
            IReadOnlyList<FinanceConsistencyWarning> warnings) =>
            new(ReceivableAmount, ReceivableOutstanding, ReceivableOverdue,
                CashReceivedThisMonth, CashReceivedYearToDate,
                ReceivedAllocatedThisMonth, ReceivedAllocatedYearToDate,
                PurchaseThisMonth, PurchaseYearToDate, PayableOutstanding,
                PayableOverdue, CashPaidThisMonth, CashPaidYearToDate,
                PaidAllocatedThisMonth, PaidAllocatedYearToDate,
                ActiveProjectContractAmount, EligibleProjectContractAmount,
                EligibleProjectPurchaseAmount, ShipmentThisMonth, warnings);
    }

    private sealed class AgingRow
    {
        public string Bucket { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public int ItemCount { get; init; }
    }

    private sealed class WarningRow
    {
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public int Count { get; init; }
    }

    private sealed class ProjectPlanRow
    {
        public ulong CustomerId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public decimal ContractAmount { get; init; }
        public decimal PlannedAmount { get; init; }
    }

    private sealed class CashFlowRow
    {
        public string Month { get; init; } = string.Empty;
        public decimal ReceivedAmount { get; init; }
        public decimal PaidAmount { get; init; }

        public FinanceCashFlowAggregateRow ToModel() =>
            new(Month, ReceivedAmount, PaidAmount);
    }

    private sealed class RiskAggregateRow
    {
        public int OverdueReceivableCount { get; init; }
        public decimal OverdueReceivableAmount { get; init; }
        public int OverduePayableCount { get; init; }
        public decimal OverduePayableAmount { get; init; }
        public int NegativeGrossProjectCount { get; init; }
        public decimal NegativeGrossProfitAmount { get; init; }

        public FinanceRiskAggregateData ToModel() =>
            new(OverdueReceivableCount, OverdueReceivableAmount,
                OverduePayableCount, OverduePayableAmount,
                NegativeGrossProjectCount, NegativeGrossProfitAmount);
    }

    private sealed class OverdueReceivableRow
    {
        public ulong ReceivableId { get; init; }
        public string ReceivableCode { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public DateOnly DueDate { get; init; }
        public int OverdueDays { get; init; }
        public decimal RemainingAmount { get; init; }

        public FinanceOverdueReceivableRow ToModel() =>
            new(ReceivableId, ReceivableCode, CustomerId, CustomerName, ProjectId,
                ProjectCode, ProjectName, DueDate, OverdueDays, RemainingAmount);
    }

    private sealed class OverduePayableRow
    {
        public ulong PayableId { get; init; }
        public string PayableCode { get; init; } = string.Empty;
        public ulong SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public DateOnly DueDate { get; init; }
        public int OverdueDays { get; init; }
        public decimal RemainingAmount { get; init; }

        public FinanceOverduePayableRow ToModel() =>
            new(PayableId, PayableCode, SupplierId, SupplierName, ProjectId,
                ProjectCode, ProjectName, DueDate, OverdueDays, RemainingAmount);
    }

    private sealed class ProjectRankingRow
    {
        public ulong ProjectId { get; init; }
        public string ProjectCode { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public ulong CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public string ProjectStatus { get; init; } = string.Empty;
        public decimal ContractAmount { get; init; }
        public decimal PurchaseAmount { get; init; }
        public decimal GrossProfit { get; init; }
        public decimal? GrossMargin { get; init; }
        public decimal ReceivableOutstandingAmount { get; init; }
        public decimal ReceivableOverdueAmount { get; init; }

        public FinanceProjectRankingRow ToModel() =>
            new(ProjectId, ProjectCode, ProjectName, CustomerId, CustomerName,
                Enum.Parse<ProjectStatus>(ProjectStatus), ContractAmount,
                PurchaseAmount, GrossProfit, GrossMargin,
                ReceivableOutstandingAmount, ReceivableOverdueAmount);
    }

    private sealed class CustomerRankingRow
    {
        public ulong CustomerId { get; init; }
        public string CustomerCode { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public decimal ContractAmount { get; init; }
        public decimal ReceivableAmount { get; init; }
        public decimal OutstandingAmount { get; init; }
        public decimal OverdueAmount { get; init; }
        public decimal CashReceivedAmount { get; init; }
        public decimal UnallocatedReceiptAmount { get; init; }

        public FinanceCustomerReceivableRankingRow ToModel() =>
            new(CustomerId, CustomerCode, CustomerName, ContractAmount,
                ReceivableAmount, OutstandingAmount, OverdueAmount,
                CashReceivedAmount, UnallocatedReceiptAmount);
    }

    private sealed class SupplierRankingRow
    {
        public ulong SupplierId { get; init; }
        public string SupplierCode { get; init; } = string.Empty;
        public string SupplierName { get; init; } = string.Empty;
        public decimal PurchaseAmount { get; init; }
        public decimal PayableAmount { get; init; }
        public decimal OutstandingAmount { get; init; }
        public decimal OverdueAmount { get; init; }
        public decimal CashPaidAmount { get; init; }
        public decimal AllocatedPaidAmount { get; init; }
        public decimal UnallocatedPaymentAmount { get; init; }

        public FinanceSupplierPayableRankingRow ToModel() =>
            new(SupplierId, SupplierCode, SupplierName, PurchaseAmount,
                PayableAmount, OutstandingAmount, OverdueAmount, CashPaidAmount,
                AllocatedPaidAmount, UnallocatedPaymentAmount);
    }
}
