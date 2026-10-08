using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Projects;
using FlowHearth.Domain.Equipment;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Projects;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlEquipmentFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task EquipmentHistoryPersistsLinksComponentsStringParametersVersionsAndAudit()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var customerService = new CustomerService(new MySqlCustomerRepository(factory), TimeProvider.System);
        var projectService = new ProjectService(new MySqlProjectRepository(factory), TimeProvider.System);
        var equipmentService = new EquipmentService(new MySqlEquipmentRepository(factory), TimeProvider.System);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await RemoveAbandonedAsync(connection);
        CustomerDetails? customer = null;
        try
        {
            var now = DateTime.UtcNow;
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand($"Phase6 MySQL {Guid.NewGuid():N}", "Phase6", null, null, null, null, null, "仅用于设备自动验证"),
                actorUserId,
                CancellationToken.None);
            var project = await projectService.CreateAsync(
                new CreateProjectCommand(customer.Id, "设备归属项目", 500000m, 20m, "Phase6 equipment parent", now.Date, now.Date.AddMonths(2)),
                actorUserId,
                CancellationToken.None);

            var creates = Enumerable.Range(1, 6).Select(index => equipmentService.CreateAsync(
                new CreateEquipmentCommand(customer.Id, project.Id, $"并发设备 {index}", EquipmentCategory.Sensor, "Test", $"M-{index}", null, null, null, "仅用于并发设备编号验证"),
                actorUserId,
                CancellationToken.None));
            var concurrentEquipment = await Task.WhenAll(creates);
            Assert.Equal(6, concurrentEquipment.Select(item => item.Code).Distinct().Count());

            var equipment = await equipmentService.CreateAsync(
                new CreateEquipmentCommand(customer.Id, project.Id, "智能装配工作站", EquipmentCategory.PLC, "Siemens", "S7-1515", "PLC-SN-001", "Assembly Line 1", now.Date, "真实数据库设备流程"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith($"EQ-{now.Year:D4}-", equipment.Code);
            Assert.Equal(project.Id, equipment.ProjectId);

            var plc = await equipmentService.CreateComponentAsync(
                equipment.Id,
                new CreateEquipmentComponentCommand(EquipmentCategory.PLC, "主控制器", "Siemens", "S7-1515", "PLC-COMP-1", "V3.1", 1, "Control cabinet", null, 10),
                actorUserId,
                CancellationToken.None);
            var servo = await equipmentService.CreateComponentAsync(
                equipment.Id,
                new CreateEquipmentComponentCommand(EquipmentCategory.Servo, "X 轴伺服", "Siemens", "S210", null, "1.2.0", 2, "Axis X", null, 20),
                actorUserId,
                CancellationToken.None);
            var parameter = await equipmentService.CreateParameterAsync(
                equipment.Id,
                new CreateEquipmentParameterCommand("Drive", "工作模式", "Auto / Manual / Jog", null, "非数值工业参数", 10),
                actorUserId,
                CancellationToken.None);
            var softwareVersion = await equipmentService.CreateVersionAsync(
                equipment.Id,
                new CreateEquipmentVersionCommand("PLC Program", "v1.0.0", "abc123def456", "Initial commissioning", now.Date, "TIA Portal V20"),
                actorUserId,
                CancellationToken.None);
            _ = await equipmentService.CreateVersionAsync(
                equipment.Id,
                new CreateEquipmentVersionCommand("HMI Project", "v1.1.0", "release/v1.1.0", "Added alarm history", now.Date.AddDays(5), null),
                actorUserId,
                CancellationToken.None);

            equipment = await equipmentService.GetAsync(equipment.Id, CancellationToken.None);
            Assert.Equal(2, equipment.Components.Count);
            Assert.Equal("Auto / Manual / Jog", Assert.Single(equipment.Parameters).Value);
            Assert.Equal(2, equipment.Versions.Count);
            Assert.Contains(equipment.Versions, item => item.GitCommit == "abc123def456" && item.Changelog == "Initial commissioning");

            await Assert.ThrowsAsync<ConflictException>(() => equipmentService.UpdateAsync(
                equipment.Id,
                new UpdateEquipmentCommand(customer.Id, project.Id, equipment.Name, equipment.Category, equipment.Manufacturer, equipment.Model, equipment.SerialNumber, equipment.InstallLocation, equipment.CommissionedDate, equipment.Notes, equipment.Version + 1),
                actorUserId,
                CancellationToken.None));
            equipment = await equipmentService.UpdateAsync(
                equipment.Id,
                new UpdateEquipmentCommand(customer.Id, project.Id, "智能装配工作站 A", equipment.Category, equipment.Manufacturer, equipment.Model, equipment.SerialNumber, equipment.InstallLocation, equipment.CommissionedDate, equipment.Notes, equipment.Version),
                actorUserId,
                CancellationToken.None);
            parameter = await equipmentService.UpdateParameterAsync(
                equipment.Id,
                parameter.Id,
                new UpdateEquipmentParameterCommand(parameter.ParameterGroup, "模拟量缩放", "0-10 V (0..27648)", "V", parameter.Notes, parameter.SortOrder, parameter.Version),
                actorUserId,
                CancellationToken.None);
            softwareVersion = await equipmentService.UpdateVersionAsync(
                equipment.Id,
                softwareVersion.Id,
                new UpdateEquipmentVersionCommand(softwareVersion.VersionType, "v1.0.1", "abc123def789", "Fixed homing sequence", softwareVersion.ReleasedDate, softwareVersion.Notes, softwareVersion.Version),
                actorUserId,
                CancellationToken.None);
            await equipmentService.DeleteComponentAsync(
                equipment.Id,
                servo.Id,
                new EquipmentVersionCommand(servo.Version),
                actorUserId,
                CancellationToken.None);

            equipment = await equipmentService.GetAsync(equipment.Id, CancellationToken.None);
            Assert.Equal(plc.Id, Assert.Single(equipment.Components).Id);
            Assert.Equal("0-10 V (0..27648)", Assert.Single(equipment.Parameters).Value);
            Assert.Contains(equipment.Versions, item => item.VersionLabel == "v1.0.1" && item.Changelog == "Fixed homing sequence");

            foreach (var query in new[] { equipment.Code, equipment.Name, "装配工作站" })
            {
                var list = await equipmentService.ListAsync(1, 20, query, "active", "PLC", customer.Id, project.Id, "code", false, CancellationToken.None);
                Assert.Equal(equipment.Id, Assert.Single(list.Items).Id);
                Assert.Equal(1, list.Total);
            }
            equipment = await equipmentService.SetArchivedAsync(equipment.Id, true, new EquipmentVersionCommand(equipment.Version), actorUserId, CancellationToken.None);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() => equipmentService.CreateParameterAsync(
                equipment.Id,
                new CreateEquipmentParameterCommand(null, "归档后新增", "blocked", null, null, 99),
                actorUserId,
                CancellationToken.None));
            equipment = await equipmentService.SetArchivedAsync(equipment.Id, false, new EquipmentVersionCommand(equipment.Version), actorUserId, CancellationToken.None);
            Assert.False(equipment.IsArchived);

            var auditCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_code=@Code;",
                new { equipment.Code });
            Assert.True(auditCount >= 11);
        }
        finally
        {
            if (customer is not null)
            {
                await DeleteCustomerTreeAsync(connection, customer.Id, customer.Code);
            }

            var residue = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM customers WHERE name LIKE 'Phase6 MySQL %' AND notes='仅用于设备自动验证';");
            Assert.Equal(0, residue);
        }
    }

    private static Task<int> DeleteCustomerTreeAsync(
        System.Data.Common.DbConnection connection,
        ulong customerId,
        string customerCode) =>
        connection.ExecuteAsync(
            """
            DELETE FROM audit_logs WHERE entity_code IN (SELECT equipment_code FROM equipment WHERE customer_id=@CustomerId) OR entity_code IN (SELECT project_code FROM projects WHERE customer_id=@CustomerId) OR entity_code=@CustomerCode;
            DELETE ec FROM equipment_components ec INNER JOIN equipment e ON e.id=ec.equipment_id WHERE e.customer_id=@CustomerId;
            DELETE ep FROM equipment_parameters ep INNER JOIN equipment e ON e.id=ep.equipment_id WHERE e.customer_id=@CustomerId;
            DELETE ev FROM equipment_versions ev INNER JOIN equipment e ON e.id=ev.equipment_id WHERE e.customer_id=@CustomerId;
            DELETE FROM equipment WHERE customer_id=@CustomerId;
            DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id WHERE p.customer_id=@CustomerId;
            DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id WHERE p.customer_id=@CustomerId;
            DELETE FROM projects WHERE customer_id=@CustomerId;
            DELETE FROM customers WHERE id=@CustomerId;
            """,
            new { CustomerId = customerId, CustomerCode = customerCode });

    private static Task<int> RemoveAbandonedAsync(System.Data.Common.DbConnection connection) =>
        connection.ExecuteAsync(
            """
            DELETE a FROM audit_logs a INNER JOIN equipment e ON e.equipment_code=a.entity_code INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE ec FROM equipment_components ec INNER JOIN equipment e ON e.id=ec.equipment_id INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE ep FROM equipment_parameters ep INNER JOIN equipment e ON e.id=ep.equipment_id INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE ev FROM equipment_versions ev INNER JOIN equipment e ON e.id=ev.equipment_id INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE e FROM equipment e INNER JOIN customers c ON c.id=e.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE p FROM projects p INNER JOIN customers c ON c.id=p.customer_id WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE a FROM audit_logs a INNER JOIN customers c ON c.customer_code=a.entity_code WHERE c.name LIKE 'Phase6 MySQL %' AND c.notes='仅用于设备自动验证';
            DELETE FROM customers WHERE name LIKE 'Phase6 MySQL %' AND notes='仅用于设备自动验证';
            """);
}
