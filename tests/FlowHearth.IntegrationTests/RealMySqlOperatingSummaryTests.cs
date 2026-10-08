using Dapper;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;
using FlowHearth.Domain.Projects;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Finance;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Settings;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlOperatingSummaryTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ReleaseReconciliationKeepsCompanyCustomerAndProjectScopesAligned()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var settings = new SettingsService(
            new MySqlSettingsRepository(factory), TimeProvider.System);
        var customerService = new CustomerService(
            new MySqlCustomerRepository(factory, settings), TimeProvider.System);
        var projectService = new ProjectService(
            new MySqlProjectRepository(factory, settings), TimeProvider.System);
        var supplierService = new SupplierService(
            new MySqlSupplierRepository(factory, settings), TimeProvider.System);
        var overviewService = new FinanceOverviewService(
            new MySqlFinanceOverviewRepository(factory, settings),
            new MySqlReceivableRepository(factory, settings),
            new MySqlPurchaseOrderRepository(factory, settings),
            settings,
            TimeProvider.System,
            new MySqlPayableRepository(factory, settings));
        await using var connection = await factory.OpenConnectionAsync(
            CancellationToken.None);
        var actor = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var beforeCompany = await overviewService.GetCompanyAsync(
            CancellationToken.None);
        var beforeDashboard = await overviewService.GetDashboardAsync(
            CancellationToken.None);
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        SupplierDetails? supplier = null;
        var marker = $"F9-{Guid.NewGuid():N}";
        var code = marker[..15];

        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"{marker} 核账客户", null, null, null, null, null, null, marker),
                actor,
                CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 核账项目", 200_000m, 0m,
                    marker, null, null),
                actor,
                CancellationToken.None);
            supplier = await supplierService.CreateAsync(
                new CreateSupplierCommand(
                    $"{marker} 核账供应商", null, SupplierStatus.Active, "F9核账",
                    null, null, null, null, null, null, null, null, null, 0,
                    null, null, marker),
                actor,
                CancellationToken.None);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = DateTime.UtcNow;
            var receivable = await InsertReceivable(
                connection, $"{code}-AR", customer.Id, project.Id, 200_000m,
                today.AddDays(30), actor, now);
            var receipt = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO receipts
                    (receipt_code,customer_id,receipt_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@CustomerId,@Today,80000,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new { Code = $"{code}-RC", CustomerId = customer.Id, Today = today, Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO receipt_allocations
                    (receipt_id,receivable_id,allocated_amount,version,created_at_utc,created_by_user_id)
                VALUES (@ReceiptId,@ReceivableId,60000,1,@Now,@Actor);
                """,
                new { ReceiptId = receipt, ReceivableId = receivable, Now = now, Actor = actor });

            var purchase = await InsertPurchase(
                connection, $"{code}-PO", supplier.Id, project.Id, today,
                "Ordered", 90_000m, actor, now);
            var payable = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO payables
                    (payable_code,supplier_id,project_id,purchase_order_id,title,payable_type,
                     amount,due_date,version,created_at_utc,created_by_user_id,
                     updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@ProjectId,@PurchaseId,'F9核账应付','PurchasePayment',
                        70000,@DueDate,1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"{code}-AP",
                    SupplierId = supplier.Id,
                    ProjectId = project.Id,
                    PurchaseId = purchase,
                    DueDate = today.AddDays(30),
                    Now = now,
                    Actor = actor,
                });
            var payment = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO payments
                    (payment_code,supplier_id,payment_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@Today,40000,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new { Code = $"{code}-PM", SupplierId = supplier.Id, Today = today, Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO payment_allocations
                    (payment_id,payable_id,allocated_amount,version,created_at_utc,created_by_user_id)
                VALUES (@PaymentId,@PayableId,30000,1,@Now,@Actor);
                """,
                new { PaymentId = payment, PayableId = payable, Now = now, Actor = actor });

            var projectSummary = await overviewService.GetProjectAsync(
                project.Id, CancellationToken.None);
            Assert.Equal(200_000m, projectSummary.ContractAmount);
            Assert.Equal(200_000m, projectSummary.ReceivableAmount);
            Assert.Equal(60_000m, projectSummary.ReceivedAllocatedAmount);
            Assert.Equal(90_000m, projectSummary.PurchaseAmount);
            Assert.Equal(70_000m, projectSummary.PayableAmount);
            Assert.Equal(30_000m, projectSummary.PaidAllocatedAmount);
            Assert.Equal(110_000m, projectSummary.GrossProfit);
            Assert.Equal(30_000m, projectSummary.CashNetInflow);

            var customerSummary = await overviewService.GetCustomerAsync(
                customer.Id, CancellationToken.None);
            Assert.Equal(80_000m, customerSummary.ReceiptAmount);
            Assert.Equal(60_000m, customerSummary.ReceivedAllocatedAmount);
            Assert.Equal(20_000m, customerSummary.UnallocatedReceiptAmount);
            Assert.Equal(90_000m, customerSummary.PurchaseAmount);
            Assert.Equal(70_000m, customerSummary.PayableAmount);
            Assert.Equal(30_000m, customerSummary.PaidAllocatedAmount);
            Assert.Equal(110_000m, customerSummary.EstimatedGrossProfit);

            var company = await overviewService.GetCompanyAsync(CancellationToken.None);
            Assert.Equal(80_000m, company.CashReceivedThisMonth - beforeCompany.CashReceivedThisMonth);
            Assert.Equal(40_000m, company.CashPaidThisMonth - beforeCompany.CashPaidThisMonth);
            Assert.Equal(30_000m, company.PaidAllocatedThisMonth - beforeCompany.PaidAllocatedThisMonth);
            Assert.Equal(90_000m, company.PurchaseThisMonth - beforeCompany.PurchaseThisMonth);
            Assert.Equal(40_000m, company.CashNetFlowThisMonth - beforeCompany.CashNetFlowThisMonth);

            var dashboard = await overviewService.GetDashboardAsync(CancellationToken.None);
            Assert.Equal(company.CashReceivedThisMonth, dashboard.Summary.CashReceivedThisMonth);
            Assert.Equal(company.CashPaidThisMonth, dashboard.Summary.CashPaidThisMonth);
            var month = today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                80_000m,
                dashboard.CashFlowTrend.Single(item => item.Month == month).ReceivedAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == month).ReceivedAmount);
            Assert.Equal(
                40_000m,
                dashboard.CashFlowTrend.Single(item => item.Month == month).PaidAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == month).PaidAmount);
        }
        finally
        {
            if (customer is not null && project is not null && supplier is not null)
            {
                await Cleanup(
                    connection, customer.Id, project.Id, project.Id,
                    project.Id, supplier.Id);
                Assert.Equal(
                    0,
                    await connection.QuerySingleAsync<int>(
                        "SELECT COUNT(*) FROM customers WHERE notes=@Marker;",
                        new { Marker = marker }));
            }
        }
    }

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task OperatingSummaryUsesTransactionFactsAndShanghaiAging()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var settings = new SettingsService(new MySqlSettingsRepository(factory), TimeProvider.System);
        var customerService = new CustomerService(
            new MySqlCustomerRepository(factory, settings), TimeProvider.System);
        var projectService = new ProjectService(
            new MySqlProjectRepository(factory, settings), TimeProvider.System);
        var supplierService = new SupplierService(
            new MySqlSupplierRepository(factory, settings), TimeProvider.System);
        var overviewService = new FinanceOverviewService(
            new MySqlFinanceOverviewRepository(factory, settings),
            new MySqlReceivableRepository(factory, settings),
            new MySqlPurchaseOrderRepository(factory, settings),
            settings,
            TimeProvider.System,
            new MySqlPayableRepository(factory, settings));
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actor = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var beforeCompany = await overviewService.GetCompanyAsync(CancellationToken.None);
        var beforeReceivableAging = await overviewService.GetReceivableAgingAsync(
            CancellationToken.None);
        var beforePayableAging = await overviewService.GetPayableAgingAsync(
            CancellationToken.None);
        var beforeDashboard = await overviewService.GetDashboardAsync(CancellationToken.None);
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        ProjectDetails? projectTwo = null;
        ProjectDetails? cancelledProject = null;
        SupplierDetails? supplier = null;
        var marker = $"F7-{Guid.NewGuid():N}";
        var code = marker[..15];
        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"{marker} 客户", null, null, null, null, null, null, marker),
                actor, CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 项目一", 1_000m, 0, marker, null, null),
                actor, CancellationToken.None);
            projectTwo = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 项目二", 500m, 0, marker, null, null),
                actor, CancellationToken.None);
            cancelledProject = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id, $"{marker} 已取消项目", 999m, 0, marker, null, null),
                actor, CancellationToken.None);
            await projectService.TransitionAsync(
                cancelledProject.Id,
                new TransitionProjectCommand(ProjectStatus.Cancelled, cancelledProject.Version),
                actor,
                CancellationToken.None);
            supplier = await supplierService.CreateAsync(
                new CreateSupplierCommand(
                    $"{marker} 供应商", null, SupplierStatus.Active, "自动测试", null,
                    null, null, null, null, null, null, null, null, 0, null, null, marker),
                actor, CancellationToken.None);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = DateTime.UtcNow;
            var receivableOne = await InsertReceivable(
                connection, $"{code}-AR1", customer.Id, project.Id, 600m,
                today.AddDays(-40), actor, now);
            await InsertReceivable(
                connection, $"{code}-AR2", customer.Id, project.Id, 400m,
                today.AddDays(1), actor, now);
            var receipt = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO receipts
                    (receipt_code,customer_id,receipt_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@CustomerId,@Today,500,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new { Code = $"{code}-RC", CustomerId = customer.Id, Today = today, Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO receipts
                    (receipt_code,customer_id,receipt_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@CustomerId,@Date,17,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                """,
                new { Code = $"{code}-RCP", CustomerId = customer.Id, Date = today.AddMonths(-1), Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO receipt_allocations
                    (receipt_id,receivable_id,allocated_amount,version,created_at_utc,created_by_user_id)
                VALUES (@ReceiptId,@ReceivableId,400,1,@Now,@Actor);
                """,
                new { ReceiptId = receipt, ReceivableId = receivableOne, Now = now, Actor = actor });

            var purchase = await InsertPurchase(
                connection, $"{code}-PO1", supplier.Id, project.Id, today,
                "Ordered", 300m, actor, now);
            await InsertPurchase(
                connection, $"{code}-PO2", supplier.Id, projectTwo.Id, today,
                "Ordered", 600m, actor, now);
            await InsertPurchase(
                connection, $"{code}-POD", supplier.Id, project.Id, today,
                "Draft", 100m, actor, now);
            await InsertPurchase(
                connection, $"{code}-POC", supplier.Id, project.Id, today,
                "Cancelled", 100m, actor, now);
            await connection.ExecuteAsync(
                """
                INSERT INTO purchase_receipts
                    (purchase_receipt_code,purchase_order_id,received_date,received_by_user_id,
                     created_at_utc,created_by_user_id)
                VALUES (@Code,@PurchaseId,@Today,@Actor,@Now,@Actor);
                """,
                new { Code = $"{code}-GR", PurchaseId = purchase, Today = today, Now = now, Actor = actor });

            var payable = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO payables
                    (payable_code,supplier_id,project_id,purchase_order_id,title,payable_type,
                     amount,due_date,version,created_at_utc,created_by_user_id,
                     updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@ProjectId,@PurchaseId,'采购应付','PurchasePayment',
                        300,@DueDate,1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"{code}-AP",
                    SupplierId = supplier.Id,
                    ProjectId = project.Id,
                    PurchaseId = purchase,
                    DueDate = today.AddDays(-10),
                    Now = now,
                    Actor = actor,
                });
            var payableTwo = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO payables
                    (payable_code,supplier_id,project_id,purchase_order_id,title,payable_type,
                     amount,due_date,version,created_at_utc,created_by_user_id,
                     updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@ProjectId,NULL,'跨项目核销应付','Other',
                        50,@DueDate,1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"{code}-AP2",
                    SupplierId = supplier.Id,
                    ProjectId = projectTwo.Id,
                    DueDate = today.AddDays(5),
                    Now = now,
                    Actor = actor,
                });
            var payment = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO payments
                    (payment_code,supplier_id,payment_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@Today,200,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                SELECT LAST_INSERT_ID();
                """,
                new { Code = $"{code}-PM", SupplierId = supplier.Id, Today = today, Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO payments
                    (payment_code,supplier_id,payment_date,amount,payment_method,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@SupplierId,@Date,9,'BankTransfer',1,@Now,@Actor,@Now,@Actor);
                """,
                new { Code = $"{code}-PMP", SupplierId = supplier.Id, Date = today.AddMonths(-1), Now = now, Actor = actor });
            await connection.ExecuteAsync(
                """
                INSERT INTO payment_allocations
                    (payment_id,payable_id,allocated_amount,version,created_at_utc,created_by_user_id)
                VALUES (@PaymentId,@PayableId,150,1,@Now,@Actor),
                       (@PaymentId,@PayableTwoId,50,1,@Now,@Actor);
                """,
                new
                {
                    PaymentId = payment,
                    PayableId = payable,
                    PayableTwoId = payableTwo,
                    Now = now,
                    Actor = actor,
                });
            await connection.ExecuteAsync(
                """
                INSERT INTO shipments
                    (shipment_code,customer_id,project_id,shipment_date,status,shipping_address,
                     signed_at_utc,version,created_at_utc,created_by_user_id,
                     updated_at_utc,updated_by_user_id)
                VALUES (@Code,@CustomerId,@ProjectId,@Today,'Received','测试地址',@Now,
                        1,@Now,@Actor,@Now,@Actor);
                """,
                new
                {
                    Code = $"{code}-SH",
                    CustomerId = customer.Id,
                    ProjectId = project.Id,
                    Today = today,
                    Now = now,
                    Actor = actor,
                });
            await connection.ExecuteAsync(
                """
                INSERT INTO shipments
                    (shipment_code,customer_id,project_id,shipment_date,status,shipping_address,
                     version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
                VALUES (@Code,@CustomerId,@ProjectId,@Today,'Cancelled','测试地址',
                        1,@Now,@Actor,@Now,@Actor);
                """,
                new
                {
                    Code = $"{code}-SHC",
                    CustomerId = customer.Id,
                    ProjectId = project.Id,
                    Today = today,
                    Now = now,
                    Actor = actor,
                });

            var projectSummary = await overviewService.GetProjectAsync(
                project.Id, CancellationToken.None);
            Assert.Equal(1_000m, projectSummary.ContractAmount);
            Assert.Equal(1_000m, projectSummary.ReceivableAmount);
            Assert.Equal(400m, projectSummary.ReceivedAllocatedAmount);
            Assert.Equal(600m, projectSummary.ReceivableOutstandingAmount);
            Assert.Equal(200m, projectSummary.ReceivableOverdueAmount);
            Assert.Equal(400m, projectSummary.ReceivableNotDueAmount);
            Assert.Equal(300m, projectSummary.PurchaseAmount);
            Assert.Equal(300m, projectSummary.PayableAmount);
            Assert.Equal(150m, projectSummary.PaidAllocatedAmount);
            Assert.Equal(150m, projectSummary.PayableOutstandingAmount);
            Assert.Equal(150m, projectSummary.PayableOverdueAmount);
            Assert.Equal(700m, projectSummary.GrossProfit);
            Assert.Equal(70m, projectSummary.GrossMargin);
            Assert.Equal(250m, projectSummary.CashNetInflow);
            Assert.Equal(1, projectSummary.PurchaseOrderCount);
            Assert.Equal(1, projectSummary.PurchaseReceiptCount);
            Assert.Equal(1, projectSummary.ShipmentCount);
            Assert.Equal(1, projectSummary.ReceivedShipmentCount);
            Assert.Equal(today, projectSummary.LastShipmentDate);
            Assert.Equal(400m, Assert.Single(projectSummary.ReceiptAllocations).AllocatedAmount);
            Assert.Equal(150m, Assert.Single(projectSummary.PaymentAllocations).AllocatedAmount);
            Assert.Equal("Received", Assert.Single(projectSummary.Shipments).Status.ToString());
            Assert.Empty(projectSummary.Warnings);

            var projectTwoSummary = await overviewService.GetProjectAsync(
                projectTwo.Id, CancellationToken.None);
            Assert.Equal(50m, projectTwoSummary.PayableAmount);
            Assert.Equal(50m, projectTwoSummary.PaidAllocatedAmount);
            Assert.Equal(0m, projectTwoSummary.PayableOutstandingAmount);
            Assert.Equal(600m, projectTwoSummary.PurchaseAmount);
            Assert.Equal(-100m, projectTwoSummary.GrossProfit);
            Assert.Equal(-50m, projectTwoSummary.CashNetInflow);

            var customerSummary = await overviewService.GetCustomerAsync(
                customer.Id, CancellationToken.None);
            Assert.Equal(2, customerSummary.ProjectCount);
            Assert.Equal(2, customerSummary.ActiveProjectCount);
            Assert.Equal(1_500m, customerSummary.ContractAmount);
            Assert.Equal(517m, customerSummary.ReceiptAmount);
            Assert.Equal(400m, customerSummary.ReceivedAllocatedAmount);
            Assert.Equal(117m, customerSummary.UnallocatedReceiptAmount);
            Assert.Equal(900m, customerSummary.PurchaseAmount);
            Assert.Equal(350m, customerSummary.PayableAmount);
            Assert.Equal(200m, customerSummary.PaidAllocatedAmount);
            Assert.Equal(600m, customerSummary.EstimatedGrossProfit);
            Assert.Equal(40m, customerSummary.EstimatedGrossMargin);
            Assert.Equal(1, customerSummary.ShipmentCount);

            var projectPage = await overviewService.ListCustomerProjectsAsync(
                customer.Id, 1, 1, "grossProfit", true, CancellationToken.None);
            Assert.Equal(2, projectPage.Total);
            Assert.Single(projectPage.Items);
            Assert.Equal(project.Id, projectPage.Items[0].ProjectId);
            Assert.Equal(700m, projectPage.Items[0].GrossProfit);

            var company = await overviewService.GetCompanyAsync(CancellationToken.None);
            Assert.Equal(500m, company.CashReceivedThisMonth - beforeCompany.CashReceivedThisMonth);
            Assert.Equal(200m, company.CashPaidThisMonth - beforeCompany.CashPaidThisMonth);
            Assert.Equal(
                200m,
                company.PaidAllocatedThisMonth - beforeCompany.PaidAllocatedThisMonth);
            Assert.Equal(300m, company.CashNetFlowThisMonth - beforeCompany.CashNetFlowThisMonth);
            Assert.Equal(900m, company.PurchaseThisMonth - beforeCompany.PurchaseThisMonth);
            Assert.Equal(1, company.ShipmentThisMonth - beforeCompany.ShipmentThisMonth);

            var dashboard = await overviewService.GetDashboardAsync(CancellationToken.None);
            Assert.Equal(company.CashReceivedThisMonth, dashboard.Summary.CashReceivedThisMonth);
            Assert.Equal(company.CashPaidThisMonth, dashboard.Summary.CashPaidThisMonth);
            Assert.Equal(12, dashboard.CashFlowTrend.Count);
            var currentMonth = today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            var previousMonth = today.AddMonths(-1).ToString(
                "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                500m,
                dashboard.CashFlowTrend.Single(item => item.Month == currentMonth).ReceivedAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == currentMonth).ReceivedAmount);
            Assert.Equal(
                200m,
                dashboard.CashFlowTrend.Single(item => item.Month == currentMonth).PaidAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == currentMonth).PaidAmount);
            Assert.Equal(
                17m,
                dashboard.CashFlowTrend.Single(item => item.Month == previousMonth).ReceivedAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == previousMonth).ReceivedAmount);
            Assert.Equal(
                9m,
                dashboard.CashFlowTrend.Single(item => item.Month == previousMonth).PaidAmount
                - beforeDashboard.CashFlowTrend.Single(item => item.Month == previousMonth).PaidAmount);
            Assert.Contains(dashboard.Risks, item => item.Code == "overdue_receivable");
            Assert.Contains(dashboard.Risks, item => item.Code == "overdue_payable");
            Assert.Contains(dashboard.Risks, item => item.Code == "negative_gross_project");
            Assert.True(dashboard.ProjectRanking.Count <= 10);
            Assert.True(dashboard.CustomerReceivableRanking.Count <= 10);
            Assert.True(dashboard.SupplierPayableRanking.Count <= 10);
            var customerRanking = Assert.Single(
                dashboard.CustomerReceivableRanking,
                item => item.CustomerId == customer.Id);
            Assert.Equal(1_500m, customerRanking.ContractAmount);
            Assert.Equal(1_000m, customerRanking.ReceivableAmount);
            Assert.Equal(600m, customerRanking.OutstandingAmount);
            Assert.Equal(200m, customerRanking.OverdueAmount);
            Assert.Equal(517m, customerRanking.CashReceivedAmount);
            Assert.Equal(117m, customerRanking.UnallocatedReceiptAmount);
            var supplierRanking = Assert.Single(
                dashboard.SupplierPayableRanking,
                item => item.SupplierId == supplier.Id);
            Assert.Equal(900m, supplierRanking.PurchaseAmount);
            Assert.Equal(350m, supplierRanking.PayableAmount);
            Assert.Equal(150m, supplierRanking.OutstandingAmount);
            Assert.Equal(150m, supplierRanking.OverdueAmount);
            Assert.Equal(209m, supplierRanking.CashPaidAmount);
            Assert.Equal(9m, supplierRanking.UnallocatedPaymentAmount);

            var grossRanking = await overviewService.GetProjectRankingAsync(
                "grossProfit", 10, CancellationToken.None);
            Assert.True(grossRanking.Count <= 10);
            Assert.True(grossRanking.Zip(grossRanking.Skip(1),
                (left, right) => left.GrossProfit >= right.GrossProfit).All(value => value));

            var receivableAging = await overviewService.GetReceivableAgingAsync(
                CancellationToken.None);
            var payableAging = await overviewService.GetPayableAgingAsync(
                CancellationToken.None);
            Assert.Equal(200m, Delta(receivableAging, beforeReceivableAging, "31-60"));
            Assert.Equal(400m, Delta(receivableAging, beforeReceivableAging, "NotDue"));
            Assert.Equal(150m, Delta(payableAging, beforePayableAging, "1-30"));

            await connection.ExecuteAsync(
                """
                UPDATE customers
                SET archived_at_utc=@Now,archived_by_user_id=@Actor
                WHERE id=@CustomerId;
                """,
                new { Now = now, Actor = actor, CustomerId = customer.Id });
            var archivedCustomerSummary = await overviewService.GetCustomerAsync(
                customer.Id, CancellationToken.None);
            Assert.Equal(1_500m, archivedCustomerSummary.ContractAmount);
        }
        finally
        {
            if (customer is not null && project is not null && projectTwo is not null
                && cancelledProject is not null && supplier is not null)
            {
                await Cleanup(
                    connection, customer.Id, project.Id, projectTwo.Id,
                    cancelledProject.Id, supplier.Id);
            }
        }
    }

    private static decimal Delta(
        FinanceAgingOverview value,
        FinanceAgingOverview baseline,
        string bucket) =>
        value.Buckets.Single(item => item.Bucket == bucket).Amount
        - baseline.Buckets.Single(item => item.Bucket == bucket).Amount;

    private static Task<ulong> InsertReceivable(
        System.Data.Common.DbConnection connection,
        string code,
        ulong customerId,
        ulong projectId,
        decimal amount,
        DateOnly dueDate,
        ulong actor,
        DateTime now) =>
        connection.QuerySingleAsync<ulong>(
            """
            INSERT INTO receivables
                (receivable_code,customer_id,project_id,title,receivable_type,amount,
                 due_date,version,created_at_utc,created_by_user_id,
                 updated_at_utc,updated_by_user_id)
            VALUES (@Code,@CustomerId,@ProjectId,'项目应收','ProgressPayment',@Amount,
                    @DueDate,1,@Now,@Actor,@Now,@Actor);
            SELECT LAST_INSERT_ID();
            """,
            new { Code = code, CustomerId = customerId, ProjectId = projectId, Amount = amount, DueDate = dueDate, Now = now, Actor = actor });

    private static Task<ulong> InsertPurchase(
        System.Data.Common.DbConnection connection,
        string code,
        ulong supplierId,
        ulong projectId,
        DateOnly date,
        string status,
        decimal amount,
        ulong actor,
        DateTime now) =>
        connection.QuerySingleAsync<ulong>(
            """
            INSERT INTO purchase_orders
                (purchase_order_code,supplier_id,project_id,order_date,status,total_amount,
                 version,created_at_utc,created_by_user_id,updated_at_utc,updated_by_user_id)
            VALUES (@Code,@SupplierId,@ProjectId,@Date,@Status,@Amount,1,@Now,@Actor,@Now,@Actor);
            SELECT LAST_INSERT_ID();
            """,
            new { Code = code, SupplierId = supplierId, ProjectId = projectId, Date = date, Status = status, Amount = amount, Now = now, Actor = actor });

    private static Task<int> Cleanup(
        System.Data.Common.DbConnection connection,
        ulong customerId,
        ulong projectId,
        ulong projectTwoId,
        ulong cancelledProjectId,
        ulong supplierId) =>
        connection.ExecuteAsync(
            """
            DELETE pa FROM payment_allocations pa INNER JOIN payables ap ON ap.id=pa.payable_id
                WHERE ap.project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM payments WHERE supplier_id=@SupplierId;
            DELETE FROM payables WHERE project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM purchase_receipts WHERE purchase_order_id IN
                (SELECT id FROM purchase_orders
                 WHERE project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId));
            DELETE FROM purchase_orders
                WHERE project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE si FROM shipment_items si INNER JOIN shipments s ON s.id=si.shipment_id
                WHERE s.project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM shipments
                WHERE project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE ra FROM receipt_allocations ra INNER JOIN receivables r ON r.id=ra.receivable_id
                WHERE r.project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM receipts WHERE customer_id=@CustomerId;
            DELETE FROM receivables
                WHERE project_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM audit_logs WHERE entity_type='project'
                AND entity_id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM projects
                WHERE id IN (@ProjectId,@ProjectTwoId,@CancelledProjectId);
            DELETE FROM audit_logs WHERE entity_type='supplier' AND entity_id=@SupplierId;
            DELETE FROM suppliers WHERE id=@SupplierId;
            DELETE FROM audit_logs WHERE entity_type='customer' AND entity_id=@CustomerId;
            DELETE FROM contacts WHERE customer_id=@CustomerId;
            DELETE FROM customers WHERE id=@CustomerId;
            """,
            new
            {
                CustomerId = customerId,
                ProjectId = projectId,
                ProjectTwoId = projectTwoId,
                CancelledProjectId = cancelledProjectId,
                SupplierId = supplierId,
            });
}
