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
public sealed class RealMySqlPayablePaymentFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task SupplierPurchasePayablePaymentAllocationFlowPreservesBalancesAndAudit()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var settings = new SettingsService(new MySqlSettingsRepository(factory), TimeProvider.System);
        var supplierService = new SupplierService(new MySqlSupplierRepository(factory, settings), TimeProvider.System);
        var customerService = new CustomerService(new MySqlCustomerRepository(factory, settings), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory, settings), TimeProvider.System);
        var purchaseRepository = new MySqlPurchaseOrderRepository(factory, settings);
        var purchaseService = new PurchaseOrderService(purchaseRepository, TimeProvider.System);
        var payableRepository = new MySqlPayableRepository(factory, settings);
        var payableService = new PayableService(payableRepository, settings, TimeProvider.System);
        var overviewService = new FinanceOverviewService(
            new MySqlFinanceOverviewRepository(factory, settings),
            new MySqlReceivableRepository(factory, settings),
            purchaseRepository,
            settings,
            TimeProvider.System,
            payableRepository);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        SupplierDetails? supplier = null;
        SupplierDetails? otherSupplier = null;
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        var purchaseIds = new List<ulong>();
        var marker = $"Payable MySQL {Guid.NewGuid():N}";
        try
        {
            supplier = await CreateSupplierAsync(supplierService, $"{marker} 供应商", actorUserId);
            otherSupplier = await CreateSupplierAsync(supplierService, $"{marker} 其他供应商", actorUserId);
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand($"{marker} 客户", null, null, null, null, null, null, "自动测试"),
                actorUserId,
                CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, $"{marker} 项目", 200_000m, 0m, "自动测试", null, null),
                actorUserId,
                CancellationToken.None);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var purchase = await CreateOrderedPurchaseAsync(
                purchaseService, supplier.Id, project.Id, today, 100_000m, actorUserId);
            purchaseIds.Add(purchase.Id);

            var payables = await payableService.CreatePayablePlanAsync(
                purchase.Id,
                new CreatePayablePlanCommand([
                    new(PayableType.AdvancePayment, "订金", 30_000m, today.AddDays(-10), null),
                    new(PayableType.DeliveryPayment, "到货款", 50_000m, today.AddDays(10), null),
                    new(PayableType.RetentionPayment, "质保款", 20_000m, today.AddDays(30), null),
                ]),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(100_000m, payables.Sum(item => item.Amount));
            Assert.StartsWith("AP-", payables[0].Code);
            Assert.True(payables[0].IsOverdue);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.CreatePayablePlanAsync(
                    purchase.Id,
                    new CreatePayablePlanCommand([
                        new(PayableType.Other, "超额", 0.01m, today, null),
                    ]),
                    actorUserId,
                    CancellationToken.None));

            var rollbackPurchase = await CreateOrderedPurchaseAsync(
                purchaseService, supplier.Id, project.Id, today, 100m, actorUserId);
            purchaseIds.Add(rollbackPurchase.Id);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.CreatePayablePlanAsync(
                    rollbackPurchase.Id,
                    new CreatePayablePlanCommand([
                        new(PayableType.AdvancePayment, "第一笔", 60m, today, null),
                        new(PayableType.DeliveryPayment, "第二笔", 50m, today, null),
                    ]),
                    actorUserId,
                    CancellationToken.None));
            var rollbackRows = await payableService.ListPayablesAsync(
                1, 100, null, "all", null, null, rollbackPurchase.Id, null, null,
                null, null, null, "dueDate", false, CancellationToken.None);
            Assert.Empty(rollbackRows.Items);

            var paymentOne = await payableService.CreatePaymentAsync(
                new CreatePaymentCommand(supplier.Id, today, 80_000m, PaymentMethod.BankTransfer,
                    supplier.Name, "BANK-F5-001", "首笔付款"),
                actorUserId,
                CancellationToken.None);
            paymentOne = await payableService.AllocatePaymentAsync(
                paymentOne.Id,
                new AllocatePaymentCommand([
                    new(payables[0].Id, 30_000m),
                    new(payables[1].Id, 40_000m),
                ], paymentOne.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(70_000m, paymentOne.AllocatedAmount);
            Assert.Equal(10_000m, paymentOne.UnallocatedAmount);

            var paymentTwo = await payableService.CreatePaymentAsync(
                new CreatePaymentCommand(supplier.Id, today, 30_000m, PaymentMethod.BankTransfer,
                    supplier.Name, "BANK-F5-002", null),
                actorUserId,
                CancellationToken.None);
            paymentTwo = await payableService.AllocatePaymentAsync(
                paymentTwo.Id,
                new AllocatePaymentCommand([
                    new(payables[1].Id, 10_000m),
                    new(payables[2].Id, 20_000m),
                ], paymentTwo.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(0m, paymentTwo.UnallocatedAmount);

            var payableOne = await payableService.GetPayableAsync(payables[0].Id, CancellationToken.None);
            var payableTwo = await payableService.GetPayableAsync(payables[1].Id, CancellationToken.None);
            Assert.Equal(PaymentStatus.Paid, payableOne.PaymentStatus);
            Assert.Equal(PaymentStatus.Paid, payableTwo.PaymentStatus);
            Assert.Equal(2, payableTwo.Allocations.Count(item => !item.IsCancelled));
            Assert.False(payableOne.IsOverdue);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.UpdatePayableAsync(
                    payableOne.Id,
                    new UpdatePayableCommand(payableOne.SupplierId, payableOne.ProjectId,
                        payableOne.PurchaseOrderId, payableOne.PayableType, payableOne.Title,
                        payableOne.AllocatedAmount - 0.01m, payableOne.DueDate, payableOne.Remark,
                        payableOne.Version),
                    actorUserId,
                    CancellationToken.None));
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.UpdatePaymentAsync(
                    paymentOne.Id,
                    new UpdatePaymentCommand(paymentOne.SupplierId, paymentOne.PaymentDate,
                        paymentOne.AllocatedAmount - 0.01m, paymentOne.PaymentMethod,
                        paymentOne.PayeeName, paymentOne.BankReference, paymentOne.Remark,
                        paymentOne.Version),
                    actorUserId,
                    CancellationToken.None));

            var overflowPayable = await payableService.CreatePayableAsync(
                new CreatePayableCommand(supplier.Id, project.Id, null, PayableType.Other,
                    "独立应付", 5_000m, today.AddDays(-1), null), actorUserId, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.AllocatePaymentAsync(
                    paymentOne.Id,
                    new AllocatePaymentCommand([new(overflowPayable.Id, 10_000.01m)], paymentOne.Version),
                    actorUserId,
                    CancellationToken.None));
            var overflowPayment = await payableService.CreatePaymentAsync(
                new CreatePaymentCommand(supplier.Id, today, 6_000m, PaymentMethod.Cash, null, null, null),
                actorUserId,
                CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.AllocatePaymentAsync(
                    overflowPayment.Id,
                    new AllocatePaymentCommand([new(overflowPayable.Id, 5_000.01m)], overflowPayment.Version),
                    actorUserId,
                    CancellationToken.None));
            var otherPayable = await payableService.CreatePayableAsync(
                new CreatePayableCommand(otherSupplier.Id, project.Id, null, PayableType.Other,
                    "其他供应商应付", 1_000m, today, null), actorUserId, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                payableService.AllocatePaymentAsync(
                    overflowPayment.Id,
                    new AllocatePaymentCommand([new(otherPayable.Id, 1_000m)], overflowPayment.Version),
                    actorUserId,
                    CancellationToken.None));

            var concurrentPayable = await payableService.CreatePayableAsync(
                new CreatePayableCommand(supplier.Id, project.Id, null, PayableType.Other,
                    "并发应付", 2_000m, today, null), actorUserId, CancellationToken.None);
            var concurrentPayment = await payableService.CreatePaymentAsync(
                new CreatePaymentCommand(supplier.Id, today, 2_000m, PaymentMethod.BankTransfer,
                    null, "BANK-F5-CONCURRENT", null), actorUserId, CancellationToken.None);
            var attempts = await Task.WhenAll(
                TryAllocateAsync(payableService, concurrentPayment, concurrentPayable.Id, actorUserId),
                TryAllocateAsync(payableService, concurrentPayment, concurrentPayable.Id, actorUserId));
            Assert.Equal(1, attempts.Count(item => item));
            concurrentPayment = await payableService.GetPaymentAsync(concurrentPayment.Id, CancellationToken.None);
            Assert.Equal(2_000m, concurrentPayment.AllocatedAmount);
            var concurrentAllocation = concurrentPayment.Allocations.Single(item => !item.IsCancelled);
            concurrentPayment = await payableService.CancelPaymentAllocationAsync(
                concurrentPayment.Id,
                concurrentAllocation.Id,
                new CancelAllocationCommand(concurrentAllocation.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(0m, concurrentPayment.AllocatedAmount);
            Assert.Equal(2_000m, concurrentPayment.UnallocatedAmount);
            Assert.Equal(2_000m, (await payableService.GetPayableAsync(
                concurrentPayable.Id, CancellationToken.None)).RemainingAmount);

            var unallocatedPayment = await payableService.CreatePaymentAsync(
                new CreatePaymentCommand(supplier.Id, today, 1_234m, PaymentMethod.Other,
                    null, "BANK-F5-UNALLOCATED", null), actorUserId, CancellationToken.None);
            var supplierFinance = await supplierService.GetAsync(supplier.Id, CancellationToken.None);
            Assert.Equal(119_234m, supplierFinance.TotalPaid);
            Assert.Equal(100_000m, supplierFinance.AllocatedPaid);
            Assert.Equal(19_234m, supplierFinance.UnallocatedPaid);
            Assert.Equal(7_000m, supplierFinance.OutstandingPayable);
            Assert.Equal(5_000m, supplierFinance.OverduePayable);
            Assert.Equal(2_000m, supplierFinance.NotDuePayable);
            var projectFinance = await overviewService.GetProjectAsync(project.Id, CancellationToken.None);
            Assert.Equal(108_000m, projectFinance.PayableAmount);
            Assert.Equal(100_000m, projectFinance.PaidAmount);
            Assert.Equal(8_000m, projectFinance.OutstandingPayableAmount);
            Assert.Equal(5_000m, projectFinance.OverduePayableAmount);
            Assert.DoesNotContain(projectFinance.Payables, item => item.Code == unallocatedPayment.Code);

            var paidPage = await payableService.ListPayablesAsync(
                1, 100, null, "active", supplier.Id, project.Id, null, null, "Paid",
                null, null, null, "paymentStatus", false, CancellationToken.None);
            var overduePage = await payableService.ListPayablesAsync(
                1, 100, null, "active", supplier.Id, project.Id, null, null, null,
                true, null, null, "dueDate", false, CancellationToken.None);
            var notOverduePage = await payableService.ListPayablesAsync(
                1, 100, null, "active", supplier.Id, project.Id, null, null, null,
                false, null, null, "dueDate", false, CancellationToken.None);
            Assert.Equal(3, paidPage.Total);
            Assert.Contains(overduePage.Items, item => item.Id == overflowPayable.Id);
            Assert.DoesNotContain(notOverduePage.Items, item => item.Id == overflowPayable.Id);
            var unallocatedPage = await payableService.ListPaymentsAsync(
                1, 100, paymentOne.Code, "active", supplier.Id, null, null,
                "BankTransfer", true, "unallocatedAmount", true, CancellationToken.None);
            Assert.Contains(unallocatedPage.Items, item => item.Id == paymentOne.Id);

            var search = new GlobalSearchService(new MySqlGlobalSearchRepository(factory));
            var payableSearch = await search.SearchAsync(payables[0].Code, 10,
                new GlobalSearchAccessScope(false, false, false, false, false, true, false),
                CancellationToken.None);
            var paymentSearch = await search.SearchAsync(paymentOne.Code, 10,
                new GlobalSearchAccessScope(false, false, false, false, false, false, true),
                CancellationToken.None);
            Assert.Contains(payableSearch, item => item.TargetType == "Payable" && item.TargetId == payables[0].Id);
            Assert.Contains(paymentSearch, item => item.TargetType == "Payment" && item.TargetId == paymentOne.Id);
            foreach (var query in new[] { payables[0].Code, supplier.Name, payables[0].Title })
            {
                var page = await payableService.ListPayablesAsync(1, 100, query, "active",
                    supplier.Id, project.Id, null, null, null, null, null, null, "dueDate", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == payables[0].Id);
                Assert.True(page.Total >= 1);
                var matches = await search.SearchAsync(query, 20,
                    new GlobalSearchAccessScope(false, false, false, false, false, true), CancellationToken.None);
                Assert.Contains(matches, item => item.Kind == "Payable" && item.TargetId == payables[0].Id);
                Assert.All(matches, item => Assert.Equal("Payable", item.Kind));
            }
            foreach (var query in new[] { paymentOne.Code, supplier.Name })
            {
                var page = await payableService.ListPaymentsAsync(1, 100, query, "active", supplier.Id,
                    null, null, null, null, "code", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == paymentOne.Id);
                Assert.True(page.Total >= 1);
                var matches = await search.SearchAsync(query, 20,
                    new GlobalSearchAccessScope(false, false, false, false, false, false, true), CancellationToken.None);
                Assert.Contains(matches, item => item.Kind == "Payment" && item.TargetId == paymentOne.Id);
                Assert.All(matches, item => Assert.Equal("Payment", item.Kind));
            }
            var auditCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_type IN ('payable','payment','payment_allocation') AND entity_code LIKE 'AP-%' OR entity_code LIKE 'PM-%';");
            Assert.True(auditCount > 0);
        }
        finally
        {
            if (project is not null && supplier is not null && otherSupplier is not null)
            {
                await CleanupAsync(connection, project.Id, supplier.Id, otherSupplier.Id);
            }

            if (customer is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM audit_logs WHERE entity_type='customer' AND entity_id=@Id; DELETE FROM contacts WHERE customer_id=@Id; DELETE FROM customers WHERE id=@Id;",
                    new { customer.Id });
            }
        }
    }

    private static async Task<SupplierDetails> CreateSupplierAsync(
        SupplierService service, string name, ulong actorUserId) =>
        await service.CreateAsync(
            new CreateSupplierCommand(name, null, SupplierStatus.Active, "自动测试", null,
                null, null, null, null, null, null, null, null, 0, null, null, "自动测试"),
            actorUserId,
            CancellationToken.None);

    private static async Task<PurchaseOrderDetails> CreateOrderedPurchaseAsync(
        PurchaseOrderService service, ulong supplierId, ulong projectId, DateOnly today,
        decimal total, ulong actorUserId)
    {
        var purchase = await service.CreateAsync(
            new CreatePurchaseOrderCommand(supplierId, projectId, today, null, null, null,
                "自动测试", [new PurchaseOrderItemCommand(null, "测试采购", null, null, null, 1m, "项", total, null)]),
            actorUserId,
            CancellationToken.None);
        return await service.OrderAsync(purchase.Id, new FinanceVersionCommand(purchase.Version),
            actorUserId, CancellationToken.None);
    }

    private static async Task<bool> TryAllocateAsync(
        PayableService service, PaymentDetails payment, ulong payableId, ulong actorUserId)
    {
        try
        {
            await service.AllocatePaymentAsync(payment.Id,
                new AllocatePaymentCommand([new(payableId, payment.Amount)], payment.Version),
                actorUserId, CancellationToken.None);
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    private static Task<int> CleanupAsync(
        System.Data.Common.DbConnection connection, ulong projectId,
        ulong supplierId, ulong otherSupplierId) => connection.ExecuteAsync(
        """
        DELETE al FROM audit_logs al INNER JOIN payment_allocations pa
          ON pa.id=al.entity_id AND al.entity_type='payment_allocation'
        INNER JOIN payables ap ON ap.id=pa.payable_id WHERE ap.project_id=@ProjectId;
        DELETE al FROM audit_logs al INNER JOIN payables ap
          ON ap.id=al.entity_id AND al.entity_type='payable' WHERE ap.project_id=@ProjectId;
        DELETE al FROM audit_logs al INNER JOIN payments pm
          ON pm.id=al.entity_id AND al.entity_type='payment'
        WHERE pm.supplier_id IN (@SupplierId,@OtherSupplierId);
        DELETE al FROM audit_logs al INNER JOIN purchase_orders po
          ON po.id=al.entity_id AND al.entity_type='purchase_order' WHERE po.project_id=@ProjectId;
        DELETE pa FROM payment_allocations pa INNER JOIN payables ap
          ON ap.id=pa.payable_id WHERE ap.project_id=@ProjectId;
        DELETE FROM payments WHERE supplier_id IN (@SupplierId,@OtherSupplierId);
        DELETE FROM payables WHERE project_id=@ProjectId;
        DELETE poi FROM purchase_order_items poi INNER JOIN purchase_orders po
          ON po.id=poi.purchase_order_id WHERE po.project_id=@ProjectId;
        DELETE FROM purchase_orders WHERE project_id=@ProjectId;
        DELETE FROM audit_logs WHERE entity_type='project' AND entity_id=@ProjectId;
        DELETE FROM projects WHERE id=@ProjectId;
        DELETE FROM audit_logs WHERE entity_type='supplier' AND entity_id IN (@SupplierId,@OtherSupplierId);
        DELETE FROM suppliers WHERE id IN (@SupplierId,@OtherSupplierId);
        """,
        new { ProjectId = projectId, SupplierId = supplierId, OtherSupplierId = otherSupplierId });
}
