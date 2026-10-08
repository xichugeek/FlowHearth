using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Opportunities;
using FlowHearth.Domain.Opportunities;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Opportunities;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlOpportunityFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task OpportunityLifecyclePersistsStagesAuditAndTransactionalConversion()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var connectionFactory = new MySqlDbConnectionFactory(connectionString);
        var customerService = new CustomerService(
            new MySqlCustomerRepository(connectionFactory),
            TimeProvider.System);
        var opportunityService = new OpportunityService(
            new MySqlOpportunityRepository(connectionFactory),
            TimeProvider.System);
        await using var connection = await connectionFactory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await RemoveAbandonedTestsAsync(connection);
        CustomerDetails? customer = null;
        OpportunityDetails? wonOpportunity = null;
        OpportunityDetails? lostOpportunity = null;
        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"Phase4 MySQL {Guid.NewGuid():N}",
                    "Phase4",
                    "Automation",
                    null,
                    null,
                    null,
                    null,
                    "仅用于商机自动验证"),
                actorUserId,
                CancellationToken.None);

            wonOpportunity = await opportunityService.CreateAsync(
                new CreateOpportunityCommand(
                    customer.Id,
                    "自动化产线升级",
                    OpportunityStage.Lead,
                    880000m,
                    25,
                    DateTime.UtcNow.Date.AddMonths(2),
                    "真实 MySQL 商机流程"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith($"OP-{DateTime.UtcNow.Year:D4}-", wonOpportunity.Code);

            foreach (var query in new[] { wonOpportunity.Code, wonOpportunity.Title, "产线升级" })
            {
                var list = await opportunityService.ListAsync(
                    1,
                    20,
                    query,
                    "active",
                    OpportunityStage.Lead.ToString(),
                    customer.Id,
                    "expectedAmount",
                    true,
                    CancellationToken.None);
                Assert.Equal(wonOpportunity.Id, Assert.Single(list.Items).Id);
                Assert.Equal(1, list.Total);
            }

            await Assert.ThrowsAsync<ConflictException>(() =>
                opportunityService.UpdateAsync(
                    wonOpportunity.Id,
                    new UpdateOpportunityCommand(
                        customer.Id,
                        wonOpportunity.Title,
                        wonOpportunity.ExpectedAmount,
                        wonOpportunity.ProbabilityPercent,
                        wonOpportunity.ExpectedCloseDate,
                        wonOpportunity.Description,
                        999),
                    actorUserId,
                    CancellationToken.None));

            wonOpportunity = await opportunityService.UpdateAsync(
                wonOpportunity.Id,
                new UpdateOpportunityCommand(
                    customer.Id,
                    wonOpportunity.Title + " 已确认",
                    920000m,
                    60,
                    wonOpportunity.ExpectedCloseDate,
                    wonOpportunity.Description,
                    wonOpportunity.Version),
                actorUserId,
                CancellationToken.None);
            wonOpportunity = await opportunityService.TransitionAsync(
                wonOpportunity.Id,
                new TransitionOpportunityCommand(
                    OpportunityStage.Negotiation,
                    null,
                    wonOpportunity.Version),
                actorUserId,
                CancellationToken.None);
            wonOpportunity = await opportunityService.TransitionAsync(
                wonOpportunity.Id,
                new TransitionOpportunityCommand(
                    OpportunityStage.Won,
                    null,
                    wonOpportunity.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(100, wonOpportunity.ProbabilityPercent);

            var conversionTasks = Enumerable.Range(0, 2)
                .Select(_ => opportunityService.ConvertToProjectAsync(
                    wonOpportunity.Id,
                    new OpportunityVersionCommand(wonOpportunity.Version),
                    actorUserId,
                    CancellationToken.None));
            var projects = await Task.WhenAll(conversionTasks);
            Assert.Single(projects.Select(project => project.Id).Distinct());
            Assert.StartsWith($"TN-{DateTime.UtcNow.Year:D4}-", projects[0].Code);
            var idempotent = await opportunityService.ConvertToProjectAsync(
                wonOpportunity.Id,
                new OpportunityVersionCommand(wonOpportunity.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(projects[0].Id, idempotent.Id);
            var projectCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM projects WHERE source_opportunity_id = @OpportunityId;",
                new { OpportunityId = wonOpportunity.Id });
            Assert.Equal(1, projectCount);

            lostOpportunity = await opportunityService.CreateAsync(
                new CreateOpportunityCommand(
                    customer.Id,
                    "终止验证商机",
                    OpportunityStage.Qualified,
                    120000m,
                    40,
                    null,
                    null),
                actorUserId,
                CancellationToken.None);
            lostOpportunity = await opportunityService.TransitionAsync(
                lostOpportunity.Id,
                new TransitionOpportunityCommand(
                    OpportunityStage.Lost,
                    "客户取消预算",
                    lostOpportunity.Version),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(0, lostOpportunity.ProbabilityPercent);
            Assert.Equal("客户取消预算", lostOpportunity.LostReason);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                opportunityService.TransitionAsync(
                    lostOpportunity.Id,
                    new TransitionOpportunityCommand(
                        OpportunityStage.Won,
                        null,
                        lostOpportunity.Version),
                    actorUserId,
                    CancellationToken.None));

            var auditCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_code IN @Codes;",
                new { Codes = new[] { wonOpportunity.Code, lostOpportunity.Code, projects[0].Code } });
            Assert.True(auditCount >= 8);
        }
        finally
        {
            if (customer is not null)
            {
                await connection.ExecuteAsync(
                    """
                    DELETE FROM audit_logs
                    WHERE entity_code IN (
                        SELECT opportunity_code FROM opportunities WHERE customer_id = @CustomerId
                    ) OR entity_code IN (
                        SELECT project_code FROM projects WHERE customer_id = @CustomerId
                    ) OR entity_code = @CustomerCode;
                    DELETE FROM projects WHERE customer_id = @CustomerId;
                    DELETE FROM opportunities WHERE customer_id = @CustomerId;
                    DELETE FROM customers WHERE id = @CustomerId;
                    """,
                    new { CustomerId = customer.Id, CustomerCode = customer.Code });
            }

            var residualCount = await connection.QuerySingleAsync<int>(
                """
                SELECT COUNT(*)
                FROM customers
                WHERE name LIKE 'Phase4 MySQL %'
                  AND notes = '仅用于商机自动验证';
                """);
            Assert.Equal(0, residualCount);
        }
    }

    private static Task<int> RemoveAbandonedTestsAsync(System.Data.Common.DbConnection connection)
    {
        return connection.ExecuteAsync(
            """
            DELETE a FROM audit_logs AS a
            INNER JOIN customers AS c ON c.customer_code = a.entity_code
            WHERE c.name LIKE 'Phase4 MySQL %'
              AND c.notes = '仅用于商机自动验证';

            DELETE a FROM audit_logs AS a
            INNER JOIN opportunities AS o ON o.opportunity_code = a.entity_code
            INNER JOIN customers AS c ON c.id = o.customer_id
            WHERE c.name LIKE 'Phase4 MySQL %'
              AND c.notes = '仅用于商机自动验证';

            DELETE a FROM audit_logs AS a
            INNER JOIN projects AS p ON p.project_code = a.entity_code
            INNER JOIN customers AS c ON c.id = p.customer_id
            WHERE c.name LIKE 'Phase4 MySQL %'
              AND c.notes = '仅用于商机自动验证';

            DELETE p FROM projects AS p
            INNER JOIN customers AS c ON c.id = p.customer_id
            WHERE c.name LIKE 'Phase4 MySQL %'
              AND c.notes = '仅用于商机自动验证';

            DELETE o FROM opportunities AS o
            INNER JOIN customers AS c ON c.id = o.customer_id
            WHERE c.name LIKE 'Phase4 MySQL %'
              AND c.notes = '仅用于商机自动验证';

            DELETE FROM customers
            WHERE name LIKE 'Phase4 MySQL %'
              AND notes = '仅用于商机自动验证';
            """);
    }
}
