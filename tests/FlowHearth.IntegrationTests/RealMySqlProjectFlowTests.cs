using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Projects;
using FlowHearth.Domain.Projects;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Projects;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlProjectFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ProjectLifecyclePersistsMembersMilestonesAuditAndConcurrency()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var factory = new MySqlDbConnectionFactory(connectionString);
        var customerService = new CustomerService(new MySqlCustomerRepository(factory), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory), TimeProvider.System);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>("SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await RemoveAbandonedAsync(connection);
        CustomerDetails? customer = null;
        ProjectDetails? project = null;
        ulong testUserId = 0;
        try
        {
            var now = DateTime.UtcNow;
            var testUsername = $"phase5_{Guid.NewGuid():N}";
            testUserId = await connection.QuerySingleAsync<ulong>(
                """
                INSERT INTO users
                    (username, normalized_username, display_name, password_hash,
                     is_active, password_changed_at_utc, security_version, version,
                     created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id)
                VALUES (@Username, @NormalizedUsername, 'Phase5 Test Member', 'not-a-login-hash',
                        1, @NowUtc, 1, 1, @NowUtc, @ActorUserId, @NowUtc, @ActorUserId);
                SELECT LAST_INSERT_ID();
                """,
                new { Username = testUsername, NormalizedUsername = testUsername.ToUpperInvariant(), NowUtc = now, ActorUserId = actorUserId });
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand($"Phase5 MySQL {Guid.NewGuid():N}", "Phase5", null, null, null, null, null, "仅用于项目自动验证"),
                actorUserId,
                CancellationToken.None);

            var creates = Enumerable.Range(1, 6).Select(index => projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, $"并发项目 {index}", 1000m * index, 0, "仅用于并发项目编号验证", null, null),
                actorUserId,
                CancellationToken.None));
            var concurrentProjects = await Task.WhenAll(creates);
            Assert.Equal(6, concurrentProjects.Select(item => item.Code).Distinct().Count());

            project = await projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, "智能产线交付", 980000m, 10m, "真实数据库项目流程", now.Date, now.Date.AddMonths(3)),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith($"TN-{now.Year:D4}-", project.Code);

            await projectService.CreateMemberAsync(project.Id, new CreateProjectMemberCommand(actorUserId, "项目经理", "总体协调"), actorUserId, CancellationToken.None);
            await projectService.CreateMemberAsync(project.Id, new CreateProjectMemberCommand(testUserId, "自动化工程师", "程序与调试"), actorUserId, CancellationToken.None);
            var milestone1 = await projectService.CreateMilestoneAsync(project.Id, new CreateProjectMilestoneCommand("方案确认", now.Date.AddDays(20), false, null, 10), actorUserId, CancellationToken.None);
            var milestone2 = await projectService.CreateMilestoneAsync(project.Id, new CreateProjectMilestoneCommand("现场验收", now.Date.AddMonths(3), false, null, 20), actorUserId, CancellationToken.None);
            project = await projectService.GetAsync(project.Id, CancellationToken.None);
            Assert.Equal(2, project.Members.Count);
            Assert.Equal(2, project.Milestones.Count);

            project = await projectService.TransitionAsync(project.Id, new TransitionProjectCommand(ProjectStatus.Active, project.Version), actorUserId, CancellationToken.None);
            project = await projectService.UpdateAsync(project.Id, new UpdateProjectCommand(customer.Id, project.Name, project.ContractAmount, 80m, project.Description, project.PlannedStartDate, project.PlannedEndDate, project.Version), actorUserId, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => projectService.TransitionAsync(project.Id, new TransitionProjectCommand(ProjectStatus.Completed, project.Version), actorUserId, CancellationToken.None));

            milestone1 = await projectService.UpdateMilestoneAsync(project.Id, milestone1.Id, new UpdateProjectMilestoneCommand(milestone1.Name, milestone1.DueDate, true, milestone1.Notes, milestone1.SortOrder, milestone1.Version), actorUserId, CancellationToken.None);
            milestone2 = await projectService.UpdateMilestoneAsync(project.Id, milestone2.Id, new UpdateProjectMilestoneCommand(milestone2.Name, milestone2.DueDate, true, milestone2.Notes, milestone2.SortOrder, milestone2.Version), actorUserId, CancellationToken.None);
            project = await projectService.GetAsync(project.Id, CancellationToken.None);
            project = await projectService.TransitionAsync(project.Id, new TransitionProjectCommand(ProjectStatus.Completed, project.Version), actorUserId, CancellationToken.None);
            Assert.Equal(100m, project.ProgressPercent);
            Assert.NotNull(project.ActualStartDate);
            Assert.NotNull(project.ActualEndDate);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => projectService.CreateMilestoneAsync(project.Id, new CreateProjectMilestoneCommand("结项后新增", null, false, null, 30), actorUserId, CancellationToken.None));

            foreach (var query in new[] { project.Code, project.Name, "产线交付" })
            {
                var list = await projectService.ListAsync(1, 20, query, "active", "Completed", customer.Id, "progress", true, CancellationToken.None);
                Assert.Equal(project.Id, Assert.Single(list.Items).Id);
                Assert.Equal(1, list.Total);
            }
            var auditCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM audit_logs WHERE entity_code=@Code;", new { project.Code });
            Assert.True(auditCount >= 9);
        }
        finally
        {
            if (customer is not null)
            {
                await connection.ExecuteAsync(
                    """
                    DELETE FROM audit_logs WHERE entity_code IN (SELECT project_code FROM projects WHERE customer_id=@CustomerId) OR entity_code=@CustomerCode;
                    DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id WHERE p.customer_id=@CustomerId;
                    DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id WHERE p.customer_id=@CustomerId;
                    DELETE FROM projects WHERE customer_id=@CustomerId;
                    DELETE FROM customers WHERE id=@CustomerId;
                    """,
                    new { CustomerId = customer.Id, CustomerCode = customer.Code });
            }
            if (testUserId != 0) await connection.ExecuteAsync("DELETE FROM users WHERE id=@Id;", new { Id = testUserId });
            var residue = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM customers WHERE name LIKE 'Phase5 MySQL %' AND notes='仅用于项目自动验证';");
            Assert.Equal(0, residue);
        }
    }

    private static Task<int> RemoveAbandonedAsync(System.Data.Common.DbConnection connection) =>
        connection.ExecuteAsync(
            """
            DELETE a FROM audit_logs a INNER JOIN projects p ON p.project_code=a.entity_code INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase5 MySQL %' AND c.notes='仅用于项目自动验证';
            DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase5 MySQL %' AND c.notes='仅用于项目自动验证';
            DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase5 MySQL %' AND c.notes='仅用于项目自动验证';
            DELETE p FROM projects p INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase5 MySQL %' AND c.notes='仅用于项目自动验证';
            DELETE a FROM audit_logs a INNER JOIN customers c ON c.customer_code=a.entity_code WHERE c.name LIKE 'Phase5 MySQL %' AND c.notes='仅用于项目自动验证';
            DELETE FROM customers WHERE name LIKE 'Phase5 MySQL %' AND notes='仅用于项目自动验证';
            DELETE FROM users WHERE username LIKE 'phase5_%' AND password_hash='not-a-login-hash';
            """);
}
