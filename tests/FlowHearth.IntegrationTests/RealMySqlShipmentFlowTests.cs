using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Search;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Equipment;
using FlowHearth.Domain.Finance;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Finance;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Search;
using FlowHearth.Infrastructure.Settings;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlShipmentFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ProjectShipmentDeliveryFlowProtectsEquipmentAndHistory()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var factory = new MySqlDbConnectionFactory(connectionString);
        var settings = new SettingsService(new MySqlSettingsRepository(factory), TimeProvider.System);
        var customerService = new CustomerService(new MySqlCustomerRepository(factory, settings), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory, settings), TimeProvider.System);
        var equipmentService = new EquipmentService(new MySqlEquipmentRepository(factory, settings), TimeProvider.System);
        var shipmentService = new ShipmentService(new MySqlShipmentRepository(factory, settings), TimeProvider.System);
        var searchService = new GlobalSearchService(new MySqlGlobalSearchRepository(factory));
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actor = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        CustomerDetails? customer = null;
        CustomerDetails? otherCustomer = null;
        ProjectDetails? project = null;
        ProjectDetails? otherProject = null;
        var marker = $"Shipment MySQL {Guid.NewGuid():N}";
        try
        {
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand($"{marker} 客户", null, null, null, null, null, null, marker),
                actor, CancellationToken.None);
            otherCustomer = await customerService.CreateAsync(
                new CreateCustomerCommand($"{marker} 其他客户", null, null, null, null, null, null, marker),
                actor, CancellationToken.None);
            project = await projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, $"{marker} 项目", 100_000m, 0, marker, null, null),
                actor, CancellationToken.None);
            otherProject = await projectService.CreateAsync(
                new CreateProjectCommand(otherCustomer.Id, $"{marker} 其他项目", 50_000m, 0, marker, null, null),
                actor, CancellationToken.None);
            var equipmentOne = await CreateEquipment(equipmentService, customer.Id, project.Id,
                $"{marker} 控制柜", actor);
            var equipmentTwo = await CreateEquipment(equipmentService, customer.Id, project.Id,
                $"{marker} 主机", actor);
            var equipmentConcurrent = await CreateEquipment(equipmentService, customer.Id, project.Id,
                $"{marker} 并发设备", actor);
            var foreignEquipment = await CreateEquipment(equipmentService, otherCustomer.Id,
                otherProject.Id, $"{marker} 外部设备", actor);
            var today = DateOnly.FromDateTime(DateTime.Today);

            await Assert.ThrowsAsync<FlowHearthValidationException>(() => shipmentService.CreateAsync(
                Command(otherCustomer.Id, project.Id, today, "跨客户", marker,
                    [Item(null, "物料")]), actor, CancellationToken.None));
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today, "跨项目设备", marker,
                    [Item(foreignEquipment.Id, foreignEquipment.Name)]), actor, CancellationToken.None));

            var shipmentOne = await shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today, "TRACK-F6-ONE", marker,
                    [
                        Item(equipmentOne.Id, equipmentOne.Name, equipmentOne.Manufacturer, equipmentOne.Model),
                        Item(null, "备用电缆", quantity: 30, unit: "米"),
                    ]), actor, CancellationToken.None);
            Assert.StartsWith("SH-", shipmentOne.Code);
            Assert.Equal(ShipmentStatus.Preparing, shipmentOne.Status);
            Assert.Equal(2, shipmentOne.Items.Count);
            shipmentOne = await shipmentService.ShipAsync(shipmentOne.Id,
                new FinanceVersionCommand(shipmentOne.Version), actor, CancellationToken.None);
            Assert.Equal(ShipmentStatus.Shipped, shipmentOne.Status);

            await Assert.ThrowsAsync<FlowHearthValidationException>(() => shipmentService.UpdateAsync(
                shipmentOne.Id,
                new UpdateShipmentCommand(shipmentOne.CustomerId, shipmentOne.ProjectId,
                    shipmentOne.ShipmentDate, shipmentOne.ReceiverName, shipmentOne.ReceiverMobile,
                    shipmentOne.LogisticsCompany, shipmentOne.TrackingNumber,
                    shipmentOne.ShippingAddress, shipmentOne.Remark,
                    shipmentOne.Items.Select(item => new ShipmentItemCommand(item.Id, item.EquipmentId,
                        item.ItemName + "篡改", item.Manufacturer, item.Model, item.Quantity, item.Unit,
                        item.Remark)).ToArray(), shipmentOne.Version),
                actor, CancellationToken.None));

            var duplicate = await shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today, "TRACK-F6-DUP", marker,
                    [Item(equipmentOne.Id, equipmentOne.Name)]), actor, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictException>(() => shipmentService.ShipAsync(
                duplicate.Id, new FinanceVersionCommand(duplicate.Version), actor,
                CancellationToken.None));

            var shipmentTwo = await shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today.AddDays(1), "TRACK-F6-TWO", marker,
                    [Item(equipmentTwo.Id, equipmentTwo.Name)]), actor, CancellationToken.None);
            shipmentTwo = await shipmentService.ShipAsync(shipmentTwo.Id,
                new FinanceVersionCommand(shipmentTwo.Version), actor, CancellationToken.None);
            shipmentTwo = await shipmentService.SetInTransitAsync(shipmentTwo.Id,
                new FinanceVersionCommand(shipmentTwo.Version), actor, CancellationToken.None);
            Assert.Equal(ShipmentStatus.InTransit, shipmentTwo.Status);

            var signedAt = DateTime.UtcNow.AddMinutes(-1);
            shipmentOne = await shipmentService.ReceiveAsync(shipmentOne.Id,
                new ReceiveShipmentCommand(signedAt, "张工", "签收测试", shipmentOne.Version),
                actor, CancellationToken.None);
            Assert.Equal(ShipmentStatus.Received, shipmentOne.Status);
            Assert.Equal("张工", shipmentOne.ReceiverName);
            Assert.NotNull(shipmentOne.SignedAtUtc);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => shipmentService.CancelAsync(
                shipmentOne.Id, new FinanceVersionCommand(shipmentOne.Version), actor,
                CancellationToken.None));
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => shipmentService.SetArchivedAsync(
                shipmentOne.Id, true, new FinanceVersionCommand(shipmentOne.Version), actor,
                CancellationToken.None));

            var concurrentA = await shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today, "TRACK-F6-CON-A", marker,
                    [Item(equipmentConcurrent.Id, equipmentConcurrent.Name)]), actor,
                CancellationToken.None);
            var concurrentB = await shipmentService.CreateAsync(
                Command(customer.Id, project.Id, today, "TRACK-F6-CON-B", marker,
                    [Item(equipmentConcurrent.Id, equipmentConcurrent.Name)]), actor,
                CancellationToken.None);
            var outcomes = await Task.WhenAll(
                TryShip(shipmentService, concurrentA, actor),
                TryShip(shipmentService, concurrentB, actor));
            Assert.Equal(1, outcomes.Count(success => success));
            var formalCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM shipment_items si
                INNER JOIN shipments sh ON sh.id=si.shipment_id
                WHERE si.equipment_id=@EquipmentId AND si.deleted_at_utc IS NULL
                  AND sh.archived_at_utc IS NULL
                  AND sh.status IN ('Shipped','InTransit','Received');
                """, new { EquipmentId = equipmentConcurrent.Id });
            Assert.Equal(1, formalCount);

            var metrics = await shipmentService.GetProjectMetricsAsync(project.Id, CancellationToken.None);
            Assert.Equal(3, metrics.ShipmentCount);
            Assert.Equal(1, metrics.ReceivedShipmentCount);
            Assert.Equal(3, metrics.EquipmentDeliveryCount);
            Assert.Equal(3, metrics.TotalEquipmentCount);
            Assert.True(metrics.AllEquipmentDelivered);
            var equipmentLookup = await shipmentService.GetEquipmentShipmentAsync(
                equipmentOne.Id, CancellationToken.None);
            Assert.True(equipmentLookup.IsShipped);
            Assert.Equal(shipmentOne.Id, equipmentLookup.Shipment?.Id);

            var codeSearch = await searchService.SearchAsync(shipmentOne.Code, 10,
                new GlobalSearchAccessScope(false, false, false, false, false, false, false, true),
                CancellationToken.None);
            var trackingSearch = await searchService.SearchAsync("TRACK-F6-ONE", 10,
                new GlobalSearchAccessScope(false, false, false, false, false, false, false, true),
                CancellationToken.None);
            Assert.Contains(codeSearch, result => result.TargetType == "Shipment" && result.TargetId == shipmentOne.Id);
            Assert.Contains(trackingSearch, result => result.TargetType == "Shipment" && result.TargetId == shipmentOne.Id);
            foreach (var query in new[] { shipmentOne.Code, customer.Name, project.Name })
            {
                var page = await shipmentService.ListAsync(1, 100, query, "active", customer.Id,
                    project.Id, null, null, null, null, "code", false, CancellationToken.None);
                Assert.Contains(page.Items, item => item.Id == shipmentOne.Id);
                Assert.True(page.Total >= 1);
                var matches = await searchService.SearchAsync(query, 20,
                    new GlobalSearchAccessScope(false, false, false, false, false, false, false, true), CancellationToken.None);
                Assert.Contains(matches, item => item.Kind == "Shipment" && item.TargetId == shipmentOne.Id);
                Assert.All(matches, item => Assert.Equal("Shipment", item.Kind));
            }
            var auditCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_type='shipment' AND entity_id=@ShipmentId;",
                new { ShipmentId = shipmentOne.Id });
            Assert.True(auditCount >= 3);
        }
        finally
        {
            if (project is not null && otherProject is not null
                && customer is not null && otherCustomer is not null)
                await Cleanup(connection, project.Id, otherProject.Id, customer.Id, otherCustomer.Id);
        }
    }

    private static CreateShipmentCommand Command(
        ulong customerId, ulong projectId, DateOnly date, string tracking,
        string marker, IReadOnlyList<ShipmentItemCommand> items) =>
        new(customerId, projectId, date, "收货人", "13800000000", "测试物流",
            tracking, "武汉市测试地址", marker, items);

    private static ShipmentItemCommand Item(
        ulong? equipmentId, string name, string? manufacturer = null,
        string? model = null, decimal quantity = 1, string unit = "台") =>
        new(null, equipmentId, name, manufacturer, model, quantity, unit, "自动测试");

    private static Task<EquipmentDetails> CreateEquipment(
        EquipmentService service, ulong customerId, ulong projectId, string name,
        ulong actor) => service.CreateAsync(
            new CreateEquipmentCommand(customerId, projectId, name, EquipmentCategory.Other,
                "FlowHearth", "F6", null, null, null, "自动测试"), actor,
            CancellationToken.None);

    private static async Task<bool> TryShip(
        ShipmentService service, ShipmentDetails shipment, ulong actor)
    {
        try
        {
            await service.ShipAsync(shipment.Id, new FinanceVersionCommand(shipment.Version),
                actor, CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is ConflictException or FlowHearthValidationException)
        {
            return false;
        }
    }

    private static Task<int> Cleanup(
        System.Data.Common.DbConnection connection, ulong projectId,
        ulong otherProjectId, ulong customerId, ulong otherCustomerId) =>
        connection.ExecuteAsync(
            """
            DELETE al FROM audit_logs al INNER JOIN shipments sh
              ON sh.id=al.entity_id AND al.entity_type='shipment'
              WHERE sh.project_id IN (@ProjectId,@OtherProjectId);
            DELETE si FROM shipment_items si INNER JOIN shipments sh
              ON sh.id=si.shipment_id WHERE sh.project_id IN (@ProjectId,@OtherProjectId);
            DELETE FROM shipments WHERE project_id IN (@ProjectId,@OtherProjectId);
            DELETE al FROM audit_logs al INNER JOIN equipment e
              ON e.id=al.entity_id AND al.entity_type='equipment'
              WHERE e.project_id IN (@ProjectId,@OtherProjectId);
            DELETE FROM equipment WHERE project_id IN (@ProjectId,@OtherProjectId);
            DELETE FROM audit_logs WHERE entity_type='project'
              AND entity_id IN (@ProjectId,@OtherProjectId);
            DELETE FROM projects WHERE id IN (@ProjectId,@OtherProjectId);
            DELETE FROM audit_logs WHERE entity_type='customer'
              AND entity_id IN (@CustomerId,@OtherCustomerId);
            DELETE FROM contacts WHERE customer_id IN (@CustomerId,@OtherCustomerId);
            DELETE FROM customers WHERE id IN (@CustomerId,@OtherCustomerId);
            """,
            new
            {
                ProjectId = projectId,
                OtherProjectId = otherProjectId,
                CustomerId = customerId,
                OtherCustomerId = otherCustomerId
            });
}
