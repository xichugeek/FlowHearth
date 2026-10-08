using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Search;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Finance;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Search;
using FlowHearth.Infrastructure.Settings;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlPurchaseFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task PurchaseOrderSupportsPartialReceiptConcurrencyAndFinanceAggregation()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var settings = new SettingsService(
            new MySqlSettingsRepository(factory),
            TimeProvider.System);
        var supplierService = new SupplierService(
            new MySqlSupplierRepository(factory, settings),
            TimeProvider.System);
        var customerService = new CustomerService(
            new MySqlCustomerRepository(factory, settings),
            TimeProvider.System);
        var projectService = new ProjectService(
            new MySqlProjectRepository(factory, settings),
            TimeProvider.System);
        var purchaseRepository = new MySqlPurchaseOrderRepository(factory, settings);
        var purchaseService = new PurchaseOrderService(
            purchaseRepository,
            TimeProvider.System);
        var overviewService = new FinanceOverviewService(
            new MySqlFinanceOverviewRepository(factory, settings),
            new MySqlReceivableRepository(factory, settings),
            purchaseRepository,
            settings,
            TimeProvider.System,
            new MySqlPayableRepository(factory, settings));
        await using var connection = await factory.OpenConnectionAsync(
            CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await CleanupAbandonedPurchasesAsync(connection);
        SupplierDetails? supplier = null;
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        PurchaseOrderDetails? purchase = null;
        var marker = $"Purchase MySQL {Guid.NewGuid():N}";
        try
        {
            supplier = await supplierService.CreateAsync(
                new CreateSupplierCommand(
                    $"{marker} 供应商", null, SupplierStatus.Active, "自动测试",
                    null, null, null, null, null, null, null, null, null, 0,
                    null, null, "仅用于采购自动验证"),
                actorUserId,
                CancellationToken.None);
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"{marker} 客户", null, null, null, null, null, null,
                    "仅用于采购自动验证"),
                actorUserId,
                CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 项目", 100_000m, 0m,
                    "仅用于采购自动验证", null, null),
                actorUserId,
                CancellationToken.None);
            var today = DateOnly.FromDateTime(DateTime.Today);
            purchase = await purchaseService.CreateAsync(
                new CreatePurchaseOrderCommand(
                    supplier.Id, project.Id, today, "测试联系人", "测试地址",
                    today.AddDays(7), "仅用于采购自动验证",
                    [
                        new PurchaseOrderItemCommand(
                            null, "控制柜", "测试制造商", "T-1", null,
                            5m, "台", 1_234.56m, null),
                        new PurchaseOrderItemCommand(
                            null, "传感器", null, "S-2", null,
                            2m, "只", 100m, null),
                    ]),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(PurchaseOrderStatus.Draft, purchase.Status);
            Assert.Equal(6_372.80m, purchase.TotalAmount);
            Assert.StartsWith("PO-", purchase.Code);

            var draftVersion = purchase.Version;
            purchase = await purchaseService.OrderAsync(
                purchase.Id,
                new FinanceVersionCommand(purchase.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(PurchaseOrderStatus.Ordered, purchase.Status);
            await Assert.ThrowsAsync<ConflictException>(() =>
                purchaseService.ReceiveAsync(
                    purchase.Id,
                    new CreatePurchaseReceiptCommand(
                        today, null,
                        [new PurchaseReceiptItemCommand(purchase.Items[0].Id, 1m)],
                        draftVersion),
                    actorUserId,
                    CancellationToken.None));

            purchase = await purchaseService.ReceiveAsync(
                purchase.Id,
                new CreatePurchaseReceiptCommand(
                    today, "首次部分收货",
                    [new PurchaseReceiptItemCommand(purchase.Items[0].Id, 2m)],
                    purchase.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(PurchaseOrderStatus.PartiallyReceived, purchase.Status);
            Assert.Equal(2m, purchase.ReceivedQuantity);
            Assert.StartsWith("GR-", purchase.Receipts.Single().Code);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                purchaseService.UpdateAsync(
                    purchase.Id,
                    new UpdatePurchaseOrderCommand(
                        purchase.SupplierId, purchase.ProjectId, purchase.OrderDate,
                        purchase.ContactName, purchase.DeliveryAddress,
                        purchase.ExpectedDeliveryDate, purchase.Remark,
                        purchase.Items.Select((item, index) =>
                            new PurchaseOrderItemCommand(
                                item.Id, item.ItemName, item.Manufacturer, item.Model,
                                item.Specification, item.Quantity, item.Unit,
                                index == 0 ? item.UnitPrice + 1m : item.UnitPrice,
                                item.Remark)).ToArray(),
                        purchase.Version),
                    actorUserId,
                    CancellationToken.None));
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                purchaseService.ReceiveAsync(
                    purchase.Id,
                    new CreatePurchaseReceiptCommand(
                        today, null,
                        [new PurchaseReceiptItemCommand(purchase.Items[0].Id, 4m)],
                        purchase.Version),
                    actorUserId,
                    CancellationToken.None));

            purchase = await purchaseService.ReceiveAsync(
                purchase.Id,
                new CreatePurchaseReceiptCommand(
                    today, "完成收货",
                    [
                        new PurchaseReceiptItemCommand(purchase.Items[0].Id, 3m),
                        new PurchaseReceiptItemCommand(purchase.Items[1].Id, 2m),
                    ],
                    purchase.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(PurchaseOrderStatus.Received, purchase.Status);
            Assert.Equal(100m, purchase.ReceiptProgress);
            Assert.Equal(2, purchase.Receipts.Count);

            var projectFinance = await overviewService.GetProjectAsync(
                project.Id,
                CancellationToken.None);
            Assert.Equal(purchase.TotalAmount, projectFinance.PurchaseAmount);
            Assert.Equal(100_000m - purchase.TotalAmount, projectFinance.ExpectedGrossMargin);
            Assert.Single(projectFinance.Purchases);
            var refreshedSupplier = await supplierService.GetAsync(
                supplier.Id,
                CancellationToken.None);
            Assert.Equal(purchase.TotalAmount, refreshedSupplier.TotalPurchased);
            var search = new GlobalSearchService(
                new MySqlGlobalSearchRepository(factory));
            var searchResults = await search.SearchAsync(
                purchase.Code,
                10,
                new GlobalSearchAccessScope(false, false, false, false, true),
                CancellationToken.None);
            Assert.Contains(
                searchResults,
                result => result.TargetType == "PurchaseOrder"
                    && result.TargetId == purchase.Id);
            foreach (var query in new[] { purchase.Code, supplier.Name, project.Name })
            {
                var page = await purchaseService.ListAsync(1, 20, query, "all", supplier.Id,
                    project.Id, null, null, null, "code", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == purchase.Id);
                Assert.True(page.Total >= 1);
                var matches = await search.SearchAsync(query, 20,
                    new GlobalSearchAccessScope(false, false, false, false, true), CancellationToken.None);
                Assert.Contains(matches, item => item.Kind == "PurchaseOrder" && item.TargetId == purchase.Id);
                Assert.All(matches, item => Assert.Equal("PurchaseOrder", item.Kind));
            }
            var auditCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_type='purchase_order' AND entity_id=@Id;",
                new { purchase.Id });
            Assert.True(auditCount >= 4);
        }
        finally
        {
            if (purchase is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE pri FROM purchase_receipt_items pri INNER JOIN purchase_receipts pr ON pr.id=pri.purchase_receipt_id WHERE pr.purchase_order_id=@Id; DELETE FROM audit_logs WHERE (entity_type='purchase_order' AND entity_id=@Id) OR (entity_type='purchase_receipt' AND JSON_EXTRACT(after_json,'$.purchaseOrderId')=@Id); DELETE FROM purchase_receipts WHERE purchase_order_id=@Id; DELETE FROM purchase_order_items WHERE purchase_order_id=@Id; DELETE FROM purchase_orders WHERE id=@Id;",
                    new { purchase.Id });
            }
            if (project is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE pri FROM purchase_receipt_items pri INNER JOIN purchase_receipts pr ON pr.id=pri.purchase_receipt_id INNER JOIN purchase_orders po ON po.id=pr.purchase_order_id WHERE po.project_id=@Id; DELETE al FROM audit_logs al INNER JOIN purchase_orders po ON po.id=al.entity_id AND al.entity_type='purchase_order' WHERE po.project_id=@Id; DELETE pr FROM purchase_receipts pr INNER JOIN purchase_orders po ON po.id=pr.purchase_order_id WHERE po.project_id=@Id; DELETE poi FROM purchase_order_items poi INNER JOIN purchase_orders po ON po.id=poi.purchase_order_id WHERE po.project_id=@Id; DELETE FROM purchase_orders WHERE project_id=@Id; DELETE FROM audit_logs WHERE entity_type='project' AND entity_id=@Id; DELETE FROM projects WHERE id=@Id;",
                    new { project.Id });
            }
            if (supplier is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM audit_logs WHERE entity_type='supplier' AND entity_id=@Id; DELETE FROM suppliers WHERE id=@Id;",
                    new { supplier.Id });
            }
            if (customer is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM audit_logs WHERE entity_type='customer' AND entity_id=@Id; DELETE FROM contacts WHERE customer_id=@Id; DELETE FROM customers WHERE id=@Id;",
                    new { customer.Id });
            }
        }
    }

    private static Task<int> CleanupAbandonedPurchasesAsync(
        System.Data.Common.DbConnection connection) =>
        connection.ExecuteAsync(
            """
            DELETE pri FROM purchase_receipt_items pri
            INNER JOIN purchase_receipts pr ON pr.id=pri.purchase_receipt_id
            INNER JOIN purchase_orders po ON po.id=pr.purchase_order_id
            INNER JOIN projects p ON p.id=po.project_id
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE al FROM audit_logs al
            INNER JOIN purchase_orders po ON po.id=al.entity_id
                AND al.entity_type='purchase_order'
            INNER JOIN projects p ON p.id=po.project_id
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE pr FROM purchase_receipts pr
            INNER JOIN purchase_orders po ON po.id=pr.purchase_order_id
            INNER JOIN projects p ON p.id=po.project_id
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE poi FROM purchase_order_items poi
            INNER JOIN purchase_orders po ON po.id=poi.purchase_order_id
            INNER JOIN projects p ON p.id=po.project_id
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE po FROM purchase_orders po
            INNER JOIN projects p ON p.id=po.project_id
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE al FROM audit_logs al
            INNER JOIN projects p ON p.id=al.entity_id AND al.entity_type='project'
            WHERE p.name LIKE 'Purchase MySQL %';
            DELETE al FROM audit_logs al
            INNER JOIN suppliers s ON s.id=al.entity_id AND al.entity_type='supplier'
            WHERE s.name LIKE 'Purchase MySQL %';
            DELETE al FROM audit_logs al
            INNER JOIN customers c ON c.id=al.entity_id AND al.entity_type='customer'
            WHERE c.name LIKE 'Purchase MySQL %';
            DELETE p FROM projects p WHERE p.name LIKE 'Purchase MySQL %';
            DELETE s FROM suppliers s WHERE s.name LIKE 'Purchase MySQL %';
            DELETE c FROM customers c WHERE c.name LIKE 'Purchase MySQL %';
            """);
}
