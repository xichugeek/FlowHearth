using Dapper;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlFinanceIntegrityTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task PersistedFinanceAndDeliveryFactsRespectReleaseInvariants()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync(
            CancellationToken.None);

        var violations = await connection.QuerySingleAsync<IntegrityViolationCounts>(
            """
            SELECT
                (SELECT COUNT(*) FROM (
                    SELECT ra.receipt_id
                    FROM receipt_allocations ra
                    INNER JOIN receipts r ON r.id=ra.receipt_id
                    WHERE ra.cancelled_at_utc IS NULL
                    GROUP BY ra.receipt_id, r.amount
                    HAVING SUM(ra.allocated_amount) > r.amount) x) AS ReceiptOverAllocated,
                (SELECT COUNT(*) FROM (
                    SELECT ra.receivable_id
                    FROM receipt_allocations ra
                    INNER JOIN receivables r ON r.id=ra.receivable_id
                    WHERE ra.cancelled_at_utc IS NULL
                    GROUP BY ra.receivable_id, r.amount
                    HAVING SUM(ra.allocated_amount) > r.amount) x) AS ReceivableOverAllocated,
                (SELECT COUNT(*) FROM (
                    SELECT pa.payment_id
                    FROM payment_allocations pa
                    INNER JOIN payments p ON p.id=pa.payment_id
                    WHERE pa.cancelled_at_utc IS NULL
                    GROUP BY pa.payment_id, p.amount
                    HAVING SUM(pa.allocated_amount) > p.amount) x) AS PaymentOverAllocated,
                (SELECT COUNT(*) FROM (
                    SELECT pa.payable_id
                    FROM payment_allocations pa
                    INNER JOIN payables p ON p.id=pa.payable_id
                    WHERE pa.cancelled_at_utc IS NULL
                    GROUP BY pa.payable_id, p.amount
                    HAVING SUM(pa.allocated_amount) > p.amount) x) AS PayableOverAllocated,
                (SELECT COUNT(*)
                 FROM receipt_allocations ra
                 INNER JOIN receipts rc ON rc.id=ra.receipt_id
                 INNER JOIN receivables rv ON rv.id=ra.receivable_id
                 WHERE ra.cancelled_at_utc IS NULL
                   AND rc.customer_id <> rv.customer_id) AS CrossCustomerReceiptAllocation,
                (SELECT COUNT(*)
                 FROM payment_allocations pa
                 INNER JOIN payments pm ON pm.id=pa.payment_id
                 INNER JOIN payables ap ON ap.id=pa.payable_id
                 WHERE pa.cancelled_at_utc IS NULL
                   AND pm.supplier_id <> ap.supplier_id) AS CrossSupplierPaymentAllocation,
                (SELECT COUNT(*) FROM (
                    SELECT rv.project_id
                    FROM receivables rv
                    INNER JOIN projects p ON p.id=rv.project_id
                    WHERE rv.archived_at_utc IS NULL
                    GROUP BY rv.project_id, p.contract_amount
                    HAVING SUM(rv.amount) > p.contract_amount) x) AS ReceivablePlanOverContract,
                (SELECT COUNT(*) FROM (
                    SELECT ap.purchase_order_id
                    FROM payables ap
                    INNER JOIN purchase_orders po ON po.id=ap.purchase_order_id
                    WHERE ap.archived_at_utc IS NULL
                    GROUP BY ap.purchase_order_id, po.total_amount
                    HAVING SUM(ap.amount) > po.total_amount) x) AS PayablePlanOverPurchase,
                (SELECT COUNT(*) FROM (
                    SELECT pri.purchase_order_item_id
                    FROM purchase_receipt_items pri
                    INNER JOIN purchase_order_items poi
                        ON poi.id=pri.purchase_order_item_id
                    GROUP BY pri.purchase_order_item_id, poi.quantity
                    HAVING SUM(pri.quantity_received) > poi.quantity) x) AS PurchaseReceivedOverOrdered,
                (SELECT COUNT(*) FROM (
                    SELECT si.equipment_id
                    FROM shipment_items si
                    INNER JOIN shipments s ON s.id=si.shipment_id
                    WHERE si.deleted_at_utc IS NULL
                      AND s.archived_at_utc IS NULL
                      AND s.status NOT IN ('Draft', 'Cancelled')
                    GROUP BY si.equipment_id
                    HAVING COUNT(DISTINCT s.id) > 1) x) AS EquipmentInMultipleFormalShipments;
            """);

        Assert.Equal(0, violations.ReceiptOverAllocated);
        Assert.Equal(0, violations.ReceivableOverAllocated);
        Assert.Equal(0, violations.PaymentOverAllocated);
        Assert.Equal(0, violations.PayableOverAllocated);
        Assert.Equal(0, violations.CrossCustomerReceiptAllocation);
        Assert.Equal(0, violations.CrossSupplierPaymentAllocation);
        Assert.Equal(0, violations.ReceivablePlanOverContract);
        Assert.Equal(0, violations.PayablePlanOverPurchase);
        Assert.Equal(0, violations.PurchaseReceivedOverOrdered);
        Assert.Equal(0, violations.EquipmentInMultipleFormalShipments);
    }

    private sealed class IntegrityViolationCounts
    {
        public int ReceiptOverAllocated { get; init; }

        public int ReceivableOverAllocated { get; init; }

        public int PaymentOverAllocated { get; init; }

        public int PayableOverAllocated { get; init; }

        public int CrossCustomerReceiptAllocation { get; init; }

        public int CrossSupplierPaymentAllocation { get; init; }

        public int ReceivablePlanOverContract { get; init; }

        public int PayablePlanOverPurchase { get; init; }

        public int PurchaseReceivedOverOrdered { get; init; }

        public int EquipmentInMultipleFormalShipments { get; init; }
    }
}
