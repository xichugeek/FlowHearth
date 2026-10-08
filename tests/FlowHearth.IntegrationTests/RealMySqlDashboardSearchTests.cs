using Dapper;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Search;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Dashboard;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Search;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlDashboardSearchTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 16, 30, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task DashboardAggregatesAndGlobalSearchFindsEverySupportedEntity()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var dashboard = new DashboardService(
            new MySqlDashboardRepository(factory),
            new FixedTimeProvider(NowUtc));
        var search = new GlobalSearchService(new MySqlGlobalSearchRepository(factory));
        var fullDashboardScope = new DashboardAccessScope(true, true, true, true);
        var fullSearchScope = new GlobalSearchAccessScope(true, true, true, true);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        await RemoveAbandonedAsync(connection);
        var baseline = await dashboard.GetAsync(fullDashboardScope, CancellationToken.None);
        var marker = $"P9{Guid.NewGuid():N}"[..12];
        var customerName = $"星河纺织测试有限公司 {marker}";
        ulong? customerId = null;

        try
        {
            customerId = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO customers
                    (customer_code,name,notes,version,created_at_utc,updated_at_utc)
                VALUES (@CustomerCode,@CustomerName,'Phase9 automated verification',1,@CreatedAtUtc,@CreatedAtUtc);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    CustomerCode = $"CU-{marker}",
                    CustomerName = customerName,
                    CreatedAtUtc = new DateTime(2026, 8, 31, 18, 0, 0, DateTimeKind.Utc),
                });
            await connection.ExecuteAsync(
                """
                INSERT INTO contacts
                    (customer_id,name,mobile,version,created_at_utc,updated_at_utc)
                VALUES (@CustomerId,@ContactName,@Mobile,1,@NowUtc,@NowUtc);
                INSERT INTO customer_followups
                    (customer_id,method,occurred_at_utc,summary,next_follow_up_at_utc,created_at_utc)
                VALUES
                    (@CustomerId,'Phone',@NowUtc,'今日跟进',@TodayFollowUp,@NowUtc),
                    (@CustomerId,'Phone',@NowUtc,'未来跟进',@FutureFollowUp,@NowUtc);
                """,
                new
                {
                    CustomerId = customerId.Value,
                    ContactName = $"联系人 {marker}",
                    Mobile = marker,
                    NowUtc,
                    TodayFollowUp = new DateTime(2026, 8, 31, 18, 30, 0, DateTimeKind.Utc),
                    FutureFollowUp = new DateTime(2026, 9, 2, 2, 0, 0, DateTimeKind.Utc),
                });
            _ = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO opportunities
                    (opportunity_code,customer_id,title,stage,expected_amount,
                     probability_percent,version,created_at_utc,updated_at_utc)
                VALUES (@Code,@CustomerId,@Title,'Lead',123.45,25,1,@NowUtc,@NowUtc);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"OP-{marker}",
                    CustomerId = customerId.Value,
                    Title = $"商机 {marker}",
                    NowUtc,
                });
            var projectId = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO projects
                    (project_code,customer_id,name,status,contract_amount,progress_percent,
                     planned_end_date,version,created_at_utc,updated_at_utc)
                VALUES (@Code,@CustomerId,@Name,'Active',1000,40,@PlannedEndDate,1,@NowUtc,@NowUtc);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"TN-{marker}",
                    CustomerId = customerId.Value,
                    Name = $"项目 {marker}",
                    PlannedEndDate = new DateTime(2026, 9, 10),
                    NowUtc,
                });
            var equipmentId = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO equipment
                    (equipment_code,customer_id,project_id,name,category,serial_number,
                     version,created_at_utc,updated_at_utc)
                VALUES (@Code,@CustomerId,@ProjectId,@Name,'PLC',@SerialNumber,1,@NowUtc,@NowUtc);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"EQ-{marker}",
                    CustomerId = customerId.Value,
                    ProjectId = projectId,
                    Name = $"设备 {marker}",
                    SerialNumber = marker,
                    NowUtc,
                });
            _ = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO service_tickets
                    (service_code,customer_id,project_id,equipment_id,title,priority,status,
                     reported_at_utc,version,created_at_utc,updated_at_utc)
                VALUES (@Code,@CustomerId,@ProjectId,@EquipmentId,@Title,'P1','New',
                        @NowUtc,1,@NowUtc,@NowUtc);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Code = $"SR-{marker}",
                    CustomerId = customerId.Value,
                    ProjectId = projectId,
                    EquipmentId = equipmentId,
                    Title = $"服务 {marker}",
                    NowUtc,
                });

            var snapshot = await dashboard.GetAsync(fullDashboardScope, CancellationToken.None);
            Assert.Equal(baseline.Metrics.TotalCustomers + 1, snapshot.Metrics.TotalCustomers);
            Assert.Equal(baseline.Metrics.NewCustomersThisMonth + 1, snapshot.Metrics.NewCustomersThisMonth);
            Assert.Equal(baseline.Metrics.ActiveOpportunities + 1, snapshot.Metrics.ActiveOpportunities);
            Assert.Equal(baseline.Metrics.ExpectedOpportunityAmount + 123.45m, snapshot.Metrics.ExpectedOpportunityAmount);
            Assert.Equal(baseline.Metrics.ActiveProjects + 1, snapshot.Metrics.ActiveProjects);
            Assert.Equal(baseline.Metrics.ProjectsNearingDelivery + 1, snapshot.Metrics.ProjectsNearingDelivery);
            Assert.Equal(baseline.Metrics.OpenTickets + 1, snapshot.Metrics.OpenTickets);
            Assert.Equal(baseline.Metrics.OpenPriorityTickets + 1, snapshot.Metrics.OpenPriorityTickets);
            Assert.Equal(baseline.Metrics.DueFollowUpsToday + 1, snapshot.Metrics.DueFollowUpsToday);
            Assert.Equal(baseline.Metrics.NextSevenDaysFollowUps + 1, snapshot.Metrics.NextSevenDaysFollowUps);
            Assert.Equal(
                baseline.OpportunityStages.SingleOrDefault(item => item.Key == "Lead")?.Count + 1 ?? 1,
                snapshot.OpportunityStages.Single(item => item.Key == "Lead").Count);
            Assert.Equal(
                baseline.ProjectStatuses.SingleOrDefault(item => item.Key == "Active")?.Count + 1 ?? 1,
                snapshot.ProjectStatuses.Single(item => item.Key == "Active").Count);
            Assert.Equal(
                baseline.MonthlyNewCustomers[^1].Count + 1,
                snapshot.MonthlyNewCustomers[^1].Count);

            var results = await search.SearchAsync(marker, 20, fullSearchScope, CancellationToken.None);
            Assert.Equal(
                ["Contact", "Customer", "Equipment", "Project", "ServiceTicket"],
                results.Select(item => item.Kind).OrderBy(item => item));
            Assert.All(results, item => Assert.False(item.IsArchived));
            Assert.Equal(customerId.Value, results.Single(item => item.Kind == "Contact").TargetId);
            Assert.Equal("Customer", results.Single(item => item.Kind == "Contact").TargetType);

            var customerService = new CustomerService(
                new MySqlCustomerRepository(factory),
                new FixedTimeProvider(NowUtc));
            foreach (var query in new[] { customerName, $"纺织测试有限公司 {marker}", "星河纺织", $"CU-{marker}" })
            {
                var globalMatches = await search.SearchAsync(
                    query, 20, fullSearchScope, CancellationToken.None);
                Assert.Contains(globalMatches, item => item.Kind == "Customer" && item.TargetId == customerId.Value);

                var listMatches = await customerService.ListAsync(
                    1, 20, query, "active", null, null, "name", false, null, CancellationToken.None);
                Assert.Contains(listMatches.Items, item => item.Id == customerId.Value);
            }

            var customerOnly = await search.SearchAsync(
                marker,
                20,
                new GlobalSearchAccessScope(true, false, false, false),
                CancellationToken.None);
            Assert.Equal(["Contact", "Customer"], customerOnly.Select(item => item.Kind).OrderBy(item => item));

            foreach (var (query, kind) in new[]
            {
                ($"联系人 {marker}", "Contact"),
                ($"项目 {marker}", "Project"),
                ($"设备 {marker}", "Equipment"),
                ($"服务 {marker}", "ServiceTicket"),
            })
            {
                var chineseResults = await search.SearchAsync(query, 20, fullSearchScope, CancellationToken.None);
                Assert.Equal(kind, Assert.Single(chineseResults).Kind);
            }

            var customerDashboard = await dashboard.GetAsync(
                new DashboardAccessScope(true, false, false, false),
                CancellationToken.None);
            Assert.NotNull(customerDashboard.Metrics.TotalCustomers);
            Assert.Null(customerDashboard.Metrics.ActiveOpportunities);
            Assert.Null(customerDashboard.Metrics.ActiveProjects);
            Assert.Null(customerDashboard.Metrics.OpenTickets);
        }
        finally
        {
            if (customerId.HasValue)
            {
                await DeleteCustomerTreeAsync(connection, customerId.Value);
            }

            Assert.Equal(0, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM customers WHERE notes='Phase9 automated verification';"));
        }
    }

    private static Task<int> DeleteCustomerTreeAsync(
        System.Data.Common.DbConnection connection,
        ulong customerId) =>
        connection.ExecuteAsync(
            """
            DELETE FROM service_tickets WHERE customer_id=@CustomerId;
            DELETE FROM equipment WHERE customer_id=@CustomerId;
            DELETE FROM project_milestones WHERE project_id IN (SELECT id FROM projects WHERE customer_id=@CustomerId);
            DELETE FROM project_members WHERE project_id IN (SELECT id FROM projects WHERE customer_id=@CustomerId);
            DELETE FROM projects WHERE customer_id=@CustomerId;
            DELETE FROM opportunities WHERE customer_id=@CustomerId;
            DELETE FROM customer_followups WHERE customer_id=@CustomerId;
            DELETE FROM contacts WHERE customer_id=@CustomerId;
            DELETE FROM customers WHERE id=@CustomerId;
            """,
            new { CustomerId = customerId });

    private static Task<int> RemoveAbandonedAsync(
        System.Data.Common.DbConnection connection) =>
        connection.ExecuteAsync(
            """
            DELETE s FROM service_tickets s INNER JOIN customers c ON c.id=s.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE e FROM equipment e INNER JOIN customers c ON c.id=e.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE pm FROM project_milestones pm INNER JOIN projects p ON p.id=pm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE p FROM projects p INNER JOIN customers c ON c.id=p.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE o FROM opportunities o INNER JOIN customers c ON c.id=o.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE f FROM customer_followups f INNER JOIN customers c ON c.id=f.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE ct FROM contacts ct INNER JOIN customers c ON c.id=ct.customer_id WHERE c.notes='Phase9 automated verification';
            DELETE FROM customers WHERE notes='Phase9 automated verification';
            """);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }
}
