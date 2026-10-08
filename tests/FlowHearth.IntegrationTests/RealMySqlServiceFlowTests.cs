using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Service;
using FlowHearth.Domain.Equipment;
using FlowHearth.Domain.Service;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Service;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlServiceFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ServiceLifecyclePersistsLinksResolutionTimelineAuditAndConcurrency()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var factory = new MySqlDbConnectionFactory(connectionString);
        var customerService = new CustomerService(new MySqlCustomerRepository(factory), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory), TimeProvider.System);
        var equipmentService = new EquipmentService(new MySqlEquipmentRepository(factory), TimeProvider.System);
        var service = new ServiceTicketService(new MySqlServiceTicketRepository(factory), TimeProvider.System);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>("SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await RemoveAbandonedAsync(connection);
        CustomerDetails? customer = null;
        try
        {
            var now = DateTime.UtcNow;
            customer = await customerService.CreateAsync(new CreateCustomerCommand($"Phase7 MySQL {Guid.NewGuid():N}", "Phase7", null, null, null, null, null, "仅用于服务自动验证"), actorUserId, CancellationToken.None);
            var project = await projectService.CreateAsync(new CreateProjectCommand(customer.Id, "服务归属项目", 0, 20, null, null, null), actorUserId, CancellationToken.None);
            var equipment = await equipmentService.CreateAsync(new CreateEquipmentCommand(customer.Id, project.Id, "服务设备", EquipmentCategory.PLC, "Siemens", "S7", "SERVICE-SN", null, now.Date, null), actorUserId, CancellationToken.None);

            var creates = Enumerable.Range(1, 6).Select(index => service.CreateAsync(new CreateServiceTicketCommand(customer.Id, project.Id, equipment.Id, $"并发工单 {index}", "编号验证", ServiceTicketPriority.P3, now, null), actorUserId, CancellationToken.None));
            var concurrent = await Task.WhenAll(creates);
            Assert.Equal(6, concurrent.Select(item => item.Code).Distinct().Count());

            var ticket = await service.CreateAsync(new CreateServiceTicketCommand(customer.Id, project.Id, equipment.Id, "产线异常停机", "伺服无法使能", ServiceTicketPriority.P1, now, actorUserId), actorUserId, CancellationToken.None);
            Assert.StartsWith($"SR-{now.Year:D4}-", ticket.Code);
            Assert.Single(ticket.Records);
            Assert.Equal(ServiceRecordType.Assignment, ticket.Records[0].RecordType);
            var diagnosis = await service.CreateRecordAsync(ticket.Id, new CreateServiceRecordCommand(ServiceRecordType.Diagnosis, "检查发现安全回路端子松动", 25, now.AddMinutes(10)), actorUserId, CancellationToken.None);
            _ = await service.CreateRecordAsync(ticket.Id, new CreateServiceRecordCommand(ServiceRecordType.Action, "紧固端子并复测安全回路", 35, now.AddMinutes(35)), actorUserId, CancellationToken.None);
            diagnosis = await service.UpdateRecordAsync(ticket.Id, diagnosis.Id, new UpdateServiceRecordCommand(ServiceRecordType.Diagnosis, "检查发现安全回路 X12 端子松动", 30, diagnosis.OccurredAtUtc, diagnosis.Version), actorUserId, CancellationToken.None);

            ticket = await service.GetAsync(ticket.Id, CancellationToken.None);
            ticket = await service.TransitionAsync(ticket.Id, new TransitionServiceTicketCommand(ServiceTicketStatus.InProgress, null, null, null, ticket.Version), actorUserId, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.TransitionAsync(ticket.Id, new TransitionServiceTicketCommand(ServiceTicketStatus.Resolved, "X12 端子松动", null, 90, ticket.Version), actorUserId, CancellationToken.None));
            ticket = await service.TransitionAsync(ticket.Id, new TransitionServiceTicketCommand(ServiceTicketStatus.Resolved, "X12 端子松动", "紧固端子并完成 30 次循环验证", 90, ticket.Version), actorUserId, CancellationToken.None);
            Assert.NotNull(ticket.ResolvedAtUtc);
            Assert.Equal((uint)90, ticket.DowntimeMinutes);
            Assert.Equal("紧固端子并完成 30 次循环验证", ticket.Solution);
            Assert.Contains(ticket.Records, item => item.RecordType == ServiceRecordType.StatusChange && item.ToStatus == ServiceTicketStatus.Resolved);
            ticket = await service.TransitionAsync(ticket.Id, new TransitionServiceTicketCommand(ServiceTicketStatus.Closed, null, null, null, ticket.Version), actorUserId, CancellationToken.None);
            Assert.NotNull(ticket.ClosedAtUtc);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.CreateRecordAsync(ticket.Id, new CreateServiceRecordCommand(ServiceRecordType.Note, "关闭后新增", null, now), actorUserId, CancellationToken.None));

            foreach (var query in new[] { ticket.Code, ticket.Title, "异常停机" })
            {
                var list = await service.ListAsync(1, 20, query, "active", "P1", "Closed", customer.Id, project.Id, equipment.Id, actorUserId, "code", false, CancellationToken.None);
                Assert.Equal(ticket.Id, Assert.Single(list.Items).Id);
                Assert.Equal(1, list.Total);
            }
            Assert.True(ticket.Records.Count >= 5);
            var auditCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM audit_logs WHERE entity_code=@Code;", new { ticket.Code });
            Assert.True(auditCount >= 7);
        }
        finally
        {
            if (customer is not null) await DeleteTreeAsync(connection, customer.Id, customer.Code);
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM customers WHERE name LIKE 'Phase7 MySQL %' AND notes='仅用于服务自动验证';"));
        }
    }

    private static Task<int> DeleteTreeAsync(System.Data.Common.DbConnection connection, ulong customerId, string customerCode) => connection.ExecuteAsync("""
        DELETE FROM audit_logs WHERE entity_code IN (SELECT service_code FROM service_tickets WHERE customer_id=@CustomerId) OR entity_code IN (SELECT equipment_code FROM equipment WHERE customer_id=@CustomerId) OR entity_code IN (SELECT project_code FROM projects WHERE customer_id=@CustomerId) OR entity_code=@CustomerCode;
        DELETE r FROM service_records r INNER JOIN service_tickets s ON s.id=r.service_ticket_id WHERE s.customer_id=@CustomerId;
        DELETE FROM service_tickets WHERE customer_id=@CustomerId;
        DELETE ec FROM equipment_components ec INNER JOIN equipment e ON e.id=ec.equipment_id WHERE e.customer_id=@CustomerId;
        DELETE ep FROM equipment_parameters ep INNER JOIN equipment e ON e.id=ep.equipment_id WHERE e.customer_id=@CustomerId;
        DELETE ev FROM equipment_versions ev INNER JOIN equipment e ON e.id=ev.equipment_id WHERE e.customer_id=@CustomerId;
        DELETE FROM equipment WHERE customer_id=@CustomerId;
        DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id WHERE p.customer_id=@CustomerId;
        DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id WHERE p.customer_id=@CustomerId;
        DELETE FROM projects WHERE customer_id=@CustomerId;
        DELETE FROM customers WHERE id=@CustomerId;
        """, new { CustomerId = customerId, CustomerCode = customerCode });

    private static Task<int> RemoveAbandonedAsync(System.Data.Common.DbConnection connection) => connection.ExecuteAsync("""
        DELETE a FROM audit_logs a INNER JOIN service_tickets s ON s.service_code=a.entity_code INNER JOIN customers c ON c.id=s.customer_id WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE r FROM service_records r INNER JOIN service_tickets s ON s.id=r.service_ticket_id INNER JOIN customers c ON c.id=s.customer_id WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE s FROM service_tickets s INNER JOIN customers c ON c.id=s.customer_id WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE e FROM equipment e INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE p FROM projects p INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE a FROM audit_logs a INNER JOIN customers c ON c.customer_code=a.entity_code WHERE c.name LIKE 'Phase7 MySQL %' AND c.notes='仅用于服务自动验证';
        DELETE FROM customers WHERE name LIKE 'Phase7 MySQL %' AND notes='仅用于服务自动验证';
        """);
}
