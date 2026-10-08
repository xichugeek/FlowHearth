using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Finance;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Settings;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlFinanceFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task SupplierSupportsCreateListArchiveRestoreAndAudit()
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
        var service = new SupplierService(
            new MySqlSupplierRepository(factory, settings),
            TimeProvider.System);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var marker = $"Finance Supplier {Guid.NewGuid():N}";
        SupplierDetails? supplier = null;
        try
        {
            supplier = await service.CreateAsync(
                new CreateSupplierCommand(
                    marker,
                    "财务测试",
                    SupplierStatus.Active,
                    "自动化元件",
                    "测试联系人",
                    "13800000000",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "月结",
                    30,
                    null,
                    null,
                    "仅用于财务自动验证"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("SP-", supplier.Code);
            Assert.Equal(SupplierStatus.Active, supplier.Status);

            foreach (var query in new[] { marker, supplier.Code, "财务测试", "测试联系人" })
            {
                var listed = await service.ListAsync(
                    1, 20, query, null, "active", null, "updatedAt", true,
                    CancellationToken.None);
                Assert.Contains(listed.Items, item => item.Id == supplier.Id);
                Assert.True(listed.Total >= 1);
            }

            var originalVersion = supplier.Version;
            supplier = await service.UpdateAsync(
                supplier.Id,
                new UpdateSupplierCommand(
                    marker,
                    "财务测试已编辑",
                    SupplierStatus.Active,
                    "自动化元件",
                    "更新联系人",
                    "13900000000",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "月结",
                    45,
                    null,
                    null,
                    "仅用于财务自动验证",
                    supplier.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal("更新联系人", supplier.ContactName);
            Assert.Equal(45, supplier.CreditDays);
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateAsync(
                    supplier.Id,
                    new UpdateSupplierCommand(
                        marker, null, SupplierStatus.Active, null, null, null, null, null,
                        null, null, null, null, null, 0, null, null, null, originalVersion),
                    actorUserId,
                    CancellationToken.None));

            supplier = await service.SetArchivedAsync(
                supplier.Id,
                true,
                new FinanceVersionCommand(supplier.Version),
                actorUserId,
                CancellationToken.None);
            Assert.True(supplier.IsArchived);

            supplier = await service.SetArchivedAsync(
                supplier.Id,
                false,
                new FinanceVersionCommand(supplier.Version),
                actorUserId,
                CancellationToken.None);
            Assert.False(supplier.IsArchived);

            var auditCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_type='supplier' AND entity_id=@SupplierId;",
                new { SupplierId = supplier.Id });
            Assert.Equal(4, auditCount);
        }
        finally
        {
            if (supplier is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM audit_logs WHERE entity_type='supplier' AND entity_id=@SupplierId; DELETE FROM suppliers WHERE id=@SupplierId;",
                    new { SupplierId = supplier.Id });
            }

            var residue = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM suppliers WHERE name=@Marker;",
                new { Marker = marker });
            Assert.Equal(0, residue);
        }
    }

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ReceivablesSupportPartialMultiAllocationAndExactAuditCleanup()
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
        var customerService = new CustomerService(
            new MySqlCustomerRepository(factory, settings),
            TimeProvider.System);
        var projectService = new ProjectService(
            new MySqlProjectRepository(factory, settings),
            TimeProvider.System);
        var financeService = new ReceivableService(
            new MySqlReceivableRepository(factory, settings),
            TimeProvider.System,
            settings);
        var overviewService = new FinanceOverviewService(
            new MySqlFinanceOverviewRepository(factory, settings),
            new MySqlReceivableRepository(factory, settings),
            new MySqlPurchaseOrderRepository(factory, settings),
            settings,
            TimeProvider.System,
            new MySqlPayableRepository(factory, settings));
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await CleanupAbandonedFinanceTestsAsync(connection);
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        var receivableIds = new List<ulong>();
        var receiptIds = new List<ulong>();
        var marker = $"Finance MySQL {Guid.NewGuid():N}";
        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"{marker} 客户", null, null, null, null, null, null, "仅用于财务自动验证"),
                actorUserId,
                CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 项目", 200_000m, 0m,
                    "仅用于财务自动验证", null, null),
                actorUserId,
                CancellationToken.None);
            var today = DateOnly.FromDateTime(DateTime.Now);
            var plans = new[]
            {
                ("预付款", ReceivableType.AdvancePayment, 60_000m),
                ("发货款", ReceivableType.ShipmentPayment, 80_000m),
                ("验收款", ReceivableType.AcceptancePayment, 50_000m),
                ("质保金", ReceivableType.WarrantyRetention, 10_000m),
            };
            var receivables = new List<ReceivableDetails>();
            for (var planIndex = 0; planIndex < plans.Length; planIndex++)
            {
                var plan = plans[planIndex];
                var created = await financeService.CreateReceivableAsync(
                    new CreateReceivableCommand(
                        customer.Id, project.Id, plan.Item1, plan.Item2, plan.Item3,
                        planIndex == 0 ? today.AddDays(-1) : today.AddDays(30),
                        null, "仅用于财务自动验证"),
                    actorUserId,
                    CancellationToken.None);
                receivables.Add(created);
                receivableIds.Add(created.Id);
            }

            Assert.Equal(FinanceBalanceStatus.Overdue, receivables[0].Status);
            Assert.All(receivables.Skip(1), item =>
                Assert.Equal(FinanceBalanceStatus.NotDue, item.Status));
            var initialCustomerFinance = await overviewService.GetCustomerAsync(
                customer.Id, CancellationToken.None);
            Assert.Equal(200_000m, initialCustomerFinance.Totals.ContractAmount);
            Assert.Equal(200_000m, initialCustomerFinance.Totals.ReceivableAmount);
            Assert.Equal(0m, initialCustomerFinance.Totals.ReceivedAmount);
            Assert.Equal(200_000m, initialCustomerFinance.Totals.OutstandingAmount);
            Assert.Equal(60_000m, initialCustomerFinance.Totals.OverdueAmount);
            Assert.Equal(140_000m, initialCustomerFinance.Totals.NotDueAmount);

            var firstReceipt = await financeService.CreateReceiptAsync(
                new CreateReceiptCommand(
                    customer.Id, today, 100_000m, PaymentMethod.BankTransfer,
                    $"TEST-{Guid.NewGuid():N}", customer.Name, "仅用于财务自动验证"),
                actorUserId,
                CancellationToken.None);
            receiptIds.Add(firstReceipt.Id);
            foreach (var query in new[] { receivables[0].Code, receivables[0].Title, customer.Name })
            {
                var page = await financeService.ListReceivablesAsync(1, 100, query, "active",
                    customer.Id, project.Id, null, null, null, null, null, "code", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == receivables[0].Id);
                Assert.True(page.Total >= 1);
            }
            foreach (var query in new[] { firstReceipt.Code, customer.Name })
            {
                var page = await financeService.ListReceiptsAsync(1, 100, query, "active", customer.Id,
                    null, null, null, "code", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == firstReceipt.Id);
                Assert.True(page.Total >= 1);
            }
            firstReceipt = await financeService.AllocateReceiptAsync(
                firstReceipt.Id,
                new AllocateReceiptCommand(
                    [
                        new ReceiptAllocationCommand(receivables[0].Id,60_000m),
                        new ReceiptAllocationCommand(receivables[1].Id,40_000m),
                    ],
                    firstReceipt.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(100_000m, firstReceipt.AllocatedAmount);
            Assert.Equal(0m, firstReceipt.UnallocatedAmount);
            Assert.Equal(2, firstReceipt.Allocations.Count(item => !item.IsCancelled));

            var fullyAllocatedForEdit = await financeService.GetReceivableAsync(
                receivables[0].Id, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                financeService.UpdateReceivableAsync(
                    fullyAllocatedForEdit.Id,
                    new UpdateReceivableCommand(
                        fullyAllocatedForEdit.Title,
                        fullyAllocatedForEdit.ReceivableType,
                        fullyAllocatedForEdit.AllocatedAmount - 0.01m,
                        fullyAllocatedForEdit.DueDate,
                        fullyAllocatedForEdit.Description,
                        fullyAllocatedForEdit.Remark,
                        fullyAllocatedForEdit.Version),
                    actorUserId,
                    CancellationToken.None));
            var editedReceivable = await financeService.UpdateReceivableAsync(
                fullyAllocatedForEdit.Id,
                new UpdateReceivableCommand(
                    $"{fullyAllocatedForEdit.Title}（已复核）",
                    fullyAllocatedForEdit.ReceivableType,
                    fullyAllocatedForEdit.AllocatedAmount,
                    fullyAllocatedForEdit.DueDate,
                    fullyAllocatedForEdit.Description,
                    fullyAllocatedForEdit.Remark,
                    fullyAllocatedForEdit.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(fullyAllocatedForEdit.AllocatedAmount, editedReceivable.Amount);
            Assert.Equal(FinanceBalanceStatus.Paid, editedReceivable.Status);

            var partPaid = await financeService.GetReceivableAsync(
                receivables[1].Id,
                CancellationToken.None);
            Assert.Equal(FinanceBalanceStatus.PartiallyPaid, partPaid.Status);
            Assert.Equal(40_000m, partPaid.OutstandingAmount);
            var cancelledAllocation = firstReceipt.Allocations.Single(
                item => item.ReceivableId == receivables[1].Id && !item.IsCancelled);
            firstReceipt = await financeService.CancelReceiptAllocationAsync(
                firstReceipt.Id,
                cancelledAllocation.Id,
                new CancelAllocationCommand(cancelledAllocation.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(40_000m, firstReceipt.UnallocatedAmount);
            var recalculated = await financeService.GetReceivableAsync(
                receivables[1].Id, CancellationToken.None);
            Assert.Equal(FinanceBalanceStatus.NotDue, recalculated.Status);
            Assert.Equal(80_000m, recalculated.OutstandingAmount);
            firstReceipt = await financeService.AllocateReceiptAsync(
                firstReceipt.Id,
                new AllocateReceiptCommand(
                    [new ReceiptAllocationCommand(receivables[1].Id, 40_000m)],
                    firstReceipt.Version),
                actorUserId,
                CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                financeService.AllocateReceiptAsync(
                    firstReceipt.Id,
                    new AllocateReceiptCommand(
                        [new ReceiptAllocationCommand(receivables[2].Id, 1m)],
                        firstReceipt.Version),
                    actorUserId,
                    CancellationToken.None));

            var secondReceipt = await financeService.CreateReceiptAsync(
                new CreateReceiptCommand(
                    customer.Id, today, 100_000m, PaymentMethod.BankTransfer,
                    $"TEST-{Guid.NewGuid():N}", customer.Name, "仅用于财务自动验证"),
                actorUserId,
                CancellationToken.None);
            receiptIds.Add(secondReceipt.Id);
            secondReceipt = await financeService.AllocateReceiptAsync(
                secondReceipt.Id,
                new AllocateReceiptCommand(
                    [
                        new ReceiptAllocationCommand(receivables[1].Id,40_000m),
                        new ReceiptAllocationCommand(receivables[2].Id,50_000m),
                        new ReceiptAllocationCommand(receivables[3].Id,10_000m),
                    ],
                    secondReceipt.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(0m, secondReceipt.UnallocatedAmount);

            foreach (var receivable in receivables)
            {
                var settled = await financeService.GetReceivableAsync(
                    receivable.Id,
                    CancellationToken.None);
                Assert.Equal(FinanceBalanceStatus.Paid, settled.Status);
                Assert.Equal(0m, settled.OutstandingAmount);
            }

            var unallocatedReceipt = await financeService.CreateReceiptAsync(
                new CreateReceiptCommand(
                    customer.Id, today, 12_345.67m, PaymentMethod.BankTransfer,
                    $"TEST-{Guid.NewGuid():N}", customer.Name, "未核销实收口径验证"),
                actorUserId,
                CancellationToken.None);
            receiptIds.Add(unallocatedReceipt.Id);
            var customerFinance = await overviewService.GetCustomerAsync(
                customer.Id, CancellationToken.None);
            Assert.Equal(200_000m, customerFinance.Totals.ContractAmount);
            Assert.Equal(200_000m, customerFinance.Totals.ReceivableAmount);
            Assert.Equal(200_000m, customerFinance.Totals.ReceivedAmount);
            Assert.Equal(0m, customerFinance.Totals.OutstandingAmount);
            Assert.Contains(
                customerFinance.RecentReceipts,
                item => item.Id == unallocatedReceipt.Id
                    && item.UnallocatedAmount == 12_345.67m);

            var planProject = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 应收计划", 100m, 0m,
                    "仅用于批量应收计划验证", null, null),
                actorUserId,
                CancellationToken.None);
            var planAmounts = ReceivablePlanPolicy.CalculatePercentageAmounts(
                planProject.ContractAmount, [33.33m, 33.33m, 33.34m]);
            var planned = await overviewService.CreateReceivablePlanAsync(
                planProject.Id,
                new CreateReceivablePlanCommand(
                [
                    new(ReceivableType.AdvancePayment,"第一阶段",planAmounts[0],today,null),
                    new(ReceivableType.ProgressPayment,"第二阶段",planAmounts[1],today.AddDays(1),null),
                    new(ReceivableType.AcceptancePayment,"第三阶段",planAmounts[2],today.AddDays(2),null),
                ]),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(100m, planned.Totals.ReceivableAmount);
            Assert.Equal(3, planned.Receivables.Count);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                overviewService.CreateReceivablePlanAsync(
                    planProject.Id,
                    new CreateReceivablePlanCommand(
                    [
                        new(ReceivableType.Other,"超合同一",1m,today,null),
                        new(ReceivableType.Other,"超合同二",1m,today,null),
                    ]),
                    actorUserId,
                    CancellationToken.None));
            var afterRejectedPlan = await overviewService.GetProjectAsync(
                planProject.Id, CancellationToken.None);
            Assert.Equal(3, afterRejectedPlan.Receivables.Count);
            Assert.Equal(100m, afterRejectedPlan.Totals.ReceivableAmount);

            var concurrentReceiptOne = await financeService.CreateReceiptAsync(
                new CreateReceiptCommand(
                    customer.Id, today, planAmounts[0], PaymentMethod.BankTransfer,
                    $"TEST-{Guid.NewGuid():N}", customer.Name, "并发核销验证一"),
                actorUserId, CancellationToken.None);
            var concurrentReceiptTwo = await financeService.CreateReceiptAsync(
                new CreateReceiptCommand(
                    customer.Id, today, planAmounts[0], PaymentMethod.BankTransfer,
                    $"TEST-{Guid.NewGuid():N}", customer.Name, "并发核销验证二"),
                actorUserId, CancellationToken.None);
            async Task<bool> TryConcurrentAllocationAsync(ReceiptDetails receipt)
            {
                try
                {
                    _ = await financeService.AllocateReceiptAsync(
                        receipt.Id,
                        new AllocateReceiptCommand(
                            [new ReceiptAllocationCommand(
                                afterRejectedPlan.Receivables[0].Id,planAmounts[0])],
                            receipt.Version),
                        actorUserId,
                        CancellationToken.None);
                    return true;
                }
                catch (Exception exception)
                    when (exception is FlowHearthValidationException or ConflictException)
                {
                    return false;
                }
            }

            var concurrentResults = await Task.WhenAll(
                TryConcurrentAllocationAsync(concurrentReceiptOne),
                TryConcurrentAllocationAsync(concurrentReceiptTwo));
            Assert.Single(concurrentResults, result => result);
            var concurrentlySettled = await financeService.GetReceivableAsync(
                afterRejectedPlan.Receivables[0].Id, CancellationToken.None);
            Assert.Equal(FinanceBalanceStatus.Paid, concurrentlySettled.Status);
            Assert.Equal(planAmounts[0], concurrentlySettled.AllocatedAmount);

            var auditCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_code IN @Codes;",
                new
                {
                    Codes = receivables.Select(item => item.Code)
                        .Concat([firstReceipt.Code, secondReceipt.Code])
                        .ToArray(),
                });
            Assert.True(auditCount >= 11);
        }
        finally
        {
            if (customer is not null)
            {
                await CleanupCustomerAsync(connection, customer.Id);
            }

            var residue = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM customers WHERE name=@Marker;",
                new { Marker = marker });
            Assert.Equal(0, residue);
        }
    }

    private static async Task CleanupAbandonedFinanceTestsAsync(
        System.Data.Common.DbConnection connection)
    {
        var customerIds = (await connection.QueryAsync<ulong>(
            """
            SELECT id FROM customers
            WHERE name LIKE 'Finance MySQL %'
              AND notes='仅用于财务自动验证';
            """)).ToArray();
        foreach (var customerId in customerIds)
        {
            await CleanupCustomerAsync(connection, customerId);
        }
    }

    private static async Task CleanupCustomerAsync(
        System.Data.Common.DbConnection connection,
        ulong customerId)
    {
        await connection.ExecuteAsync(
            """
            DELETE a FROM audit_logs AS a
            INNER JOIN receipt_allocations AS ra
                ON a.entity_type='receipt_allocation' AND a.entity_id=ra.id
            INNER JOIN receipts AS rc ON rc.id=ra.receipt_id
            WHERE rc.customer_id=@CustomerId;

            DELETE ra FROM receipt_allocations AS ra
            INNER JOIN receipts AS rc ON rc.id=ra.receipt_id
            WHERE rc.customer_id=@CustomerId;

            DELETE a FROM audit_logs AS a
            INNER JOIN receipts AS rc
                ON a.entity_type='receipt' AND a.entity_id=rc.id
            WHERE rc.customer_id=@CustomerId;
            DELETE FROM receipts WHERE customer_id=@CustomerId;

            DELETE a FROM audit_logs AS a
            INNER JOIN receivables AS rv
                ON a.entity_type='receivable' AND a.entity_id=rv.id
            WHERE rv.customer_id=@CustomerId;
            DELETE FROM receivables WHERE customer_id=@CustomerId;

            DELETE a FROM audit_logs AS a
            INNER JOIN projects AS p
                ON a.entity_type='project' AND a.entity_id=p.id
            WHERE p.customer_id=@CustomerId;
            DELETE FROM projects WHERE customer_id=@CustomerId;

            DELETE FROM audit_logs
            WHERE entity_type='customer' AND entity_id=@CustomerId;
            DELETE FROM customers WHERE id=@CustomerId;
            """,
            new { CustomerId = customerId });
    }
}
