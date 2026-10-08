using Dapper;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Audit;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Service;
using FlowHearth.Domain.Attachments;
using FlowHearth.Domain.Equipment;
using FlowHearth.Domain.Opportunities;
using FlowHearth.Domain.Service;
using FlowHearth.Infrastructure.Attachments;
using FlowHearth.Infrastructure.Audit;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Opportunities;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Service;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlAttachmentAuditFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task AttachmentsPersistForAllCoreEntitiesAndAuditIsQueryable()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var abandoned = (await connection.QueryAsync<CustomerMarker>(
            "SELECT id AS Id,customer_code AS Code FROM customers WHERE name LIKE 'Phase8 MySQL %' AND notes='仅用于附件自动验证';")).ToArray();
        foreach (var marker in abandoned)
        {
            await DeleteTreeAsync(connection, marker.Id, marker.Code);
        }

        var customerService = new CustomerService(new MySqlCustomerRepository(factory), TimeProvider.System);
        var opportunityService = new OpportunityService(new MySqlOpportunityRepository(factory), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory), TimeProvider.System);
        var equipmentService = new EquipmentService(new MySqlEquipmentRepository(factory), TimeProvider.System);
        var ticketService = new ServiceTicketService(new MySqlServiceTicketRepository(factory), TimeProvider.System);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"flowhearth-phase8-{Guid.NewGuid():N}");
        var storage = new LocalFileStorage(storageRoot, 20 * 1024 * 1024, TimeProvider.System);
        var attachmentService = new AttachmentService(new MySqlAttachmentRepository(factory), storage, TimeProvider.System);
        var auditService = new AuditLogService(new MySqlAuditLogRepository(factory));
        CustomerDetails? customer = null;

        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand($"Phase8 MySQL {Guid.NewGuid():N}", "Phase8", null, null, null, null, null, "仅用于附件自动验证"),
                actorUserId,
                CancellationToken.None);
            var opportunity = await opportunityService.CreateAsync(
                new CreateOpportunityCommand(customer.Id, "附件商机", OpportunityStage.Lead, 1000, 20, null, null),
                actorUserId,
                CancellationToken.None);
            var project = await projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, "附件项目", 1000, 0, null, null, null),
                actorUserId,
                CancellationToken.None);
            var equipment = await equipmentService.CreateAsync(
                new CreateEquipmentCommand(customer.Id, project.Id, "附件设备", EquipmentCategory.PLC, null, null, "PHASE8-SN", null, null, null),
                actorUserId,
                CancellationToken.None);
            var ticket = await ticketService.CreateAsync(
                new CreateServiceTicketCommand(customer.Id, project.Id, equipment.Id, "附件服务单", null, ServiceTicketPriority.P3, null, null),
                actorUserId,
                CancellationToken.None);

            var entities = new (AttachmentEntityType Type, ulong Id)[]
            {
                (AttachmentEntityType.Customer, customer.Id),
                (AttachmentEntityType.Opportunity, opportunity.Id),
                (AttachmentEntityType.Project, project.Id),
                (AttachmentEntityType.Equipment, equipment.Id),
                (AttachmentEntityType.ServiceTicket, ticket.Id),
            };
            var uploaded = new List<AttachmentSummary>();
            for (var index = 0; index < entities.Length; index++)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes($"Phase8 file {index} 中文内容");
                await using var content = new MemoryStream(bytes);
                uploaded.Add(await attachmentService.UploadAsync(
                    new UploadAttachmentCommand(
                        entities[index].Type,
                        entities[index].Id,
                        $"工程附件-{index}.txt",
                        "text/plain",
                        bytes.Length,
                        content,
                        "自动验证附件"),
                    actorUserId,
                    CancellationToken.None));
            }

            Assert.Equal(5, uploaded.Count);
            Assert.All(uploaded, item => Assert.Matches("^[a-f0-9]{64}$", item.Sha256));
            foreach (var entity in entities)
            {
                Assert.Single(await attachmentService.ListAsync(entity.Type, entity.Id, CancellationToken.None));
            }

            await using (var download = (await attachmentService.DownloadAsync(uploaded[0].Id, CancellationToken.None)).Content)
            using (var reader = new StreamReader(download))
            {
                Assert.Contains("中文内容", await reader.ReadToEndAsync(CancellationToken.None));
            }

            await Assert.ThrowsAsync<ConflictException>(() => attachmentService.DeleteAsync(
                uploaded[0].Id,
                99,
                actorUserId,
                CancellationToken.None));
            await attachmentService.DeleteAsync(uploaded[0].Id, 1, actorUserId, CancellationToken.None);
            Assert.Empty(await attachmentService.ListAsync(AttachmentEntityType.Customer, customer.Id, CancellationToken.None));

            var audits = await auditService.ListAsync(
                1,
                20,
                "工程附件",
                "Attachment",
                null,
                actorUserId,
                null,
                null,
                "occurredAt",
                true,
                CancellationToken.None);
            Assert.Equal(6, audits.Total);
            Assert.Contains(audits.Items, item => item.Action == "AttachmentDeleted");
            var detail = await auditService.GetAsync(audits.Items.First(item => item.Action == "AttachmentUploaded").Id, CancellationToken.None);
            Assert.Contains("OriginalFileName", detail.AfterJson, StringComparison.Ordinal);
            Assert.DoesNotContain("StorageKey", detail.AfterJson, StringComparison.Ordinal);

            var uploadedAudits = await auditService.ListAsync(
                1,
                20,
                null,
                "Attachment",
                "AttachmentUploaded",
                null,
                null,
                null,
                "occurredAt",
                true,
                CancellationToken.None);
            Assert.Equal(5, uploadedAudits.Total);
        }
        finally
        {
            if (customer is not null)
            {
                await DeleteTreeAsync(connection, customer.Id, customer.Code);
            }

            Assert.Equal(0, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM customers WHERE name LIKE 'Phase8 MySQL %' AND notes='仅用于附件自动验证';"));
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, true);
            }
        }
    }

    private static Task<int> DeleteTreeAsync(
        System.Data.Common.DbConnection connection,
        ulong customerId,
        string customerCode) =>
        connection.ExecuteAsync(
            """
            DELETE FROM attachments
            WHERE (entity_type='Customer' AND entity_id=@CustomerId)
               OR (entity_type='Opportunity' AND entity_id IN (SELECT id FROM opportunities WHERE customer_id=@CustomerId))
               OR (entity_type='Project' AND entity_id IN (SELECT id FROM projects WHERE customer_id=@CustomerId))
               OR (entity_type='Equipment' AND entity_id IN (SELECT id FROM equipment WHERE customer_id=@CustomerId))
               OR (entity_type='ServiceTicket' AND entity_id IN (SELECT id FROM service_tickets WHERE customer_id=@CustomerId));
            DELETE FROM audit_logs WHERE entity_code IN (SELECT opportunity_code FROM opportunities WHERE customer_id=@CustomerId)
               OR entity_code IN (SELECT project_code FROM projects WHERE customer_id=@CustomerId)
               OR entity_code IN (SELECT equipment_code FROM equipment WHERE customer_id=@CustomerId)
               OR entity_code IN (SELECT service_code FROM service_tickets WHERE customer_id=@CustomerId)
               OR entity_code=@CustomerCode;
            DELETE r FROM service_records r INNER JOIN service_tickets s ON s.id=r.service_ticket_id WHERE s.customer_id=@CustomerId;
            DELETE FROM service_tickets WHERE customer_id=@CustomerId;
            DELETE FROM equipment WHERE customer_id=@CustomerId;
            DELETE FROM project_members WHERE project_id IN (SELECT id FROM projects WHERE customer_id=@CustomerId);
            DELETE FROM project_milestones WHERE project_id IN (SELECT id FROM projects WHERE customer_id=@CustomerId);
            DELETE FROM projects WHERE customer_id=@CustomerId;
            DELETE FROM opportunities WHERE customer_id=@CustomerId;
            DELETE FROM customers WHERE id=@CustomerId;
            """,
            new { CustomerId = customerId, CustomerCode = customerCode });

    private sealed class CustomerMarker
    {
        public ulong Id { get; init; }
        public string Code { get; init; } = string.Empty;
    }
}
