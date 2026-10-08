using Dapper;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Service;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Equipment;
using FlowHearth.Domain.Opportunities;
using FlowHearth.Domain.Service;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Opportunities;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Service;
using FlowHearth.Infrastructure.Settings;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlSettingsTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task DictionariesSettingsAuditsAndConfiguredPrefixPersist()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var repository = new MySqlSettingsRepository(factory);
        var settingsService = new SettingsService(
            repository,
            new FixedTimeProvider(NowUtc));
        await using var connection = await factory.OpenConnectionAsync(
            CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var auditBaseline = await connection.QuerySingleAsync<ulong>(
            "SELECT COALESCE(MAX(id),0) FROM audit_logs;");
        var mutableSettingKeys = new[]
        {
            SystemSettingKeys.DefaultPageSize,
            SystemSettingKeys.CustomerNumberPrefix,
            SystemSettingKeys.OpportunityNumberPrefix,
            SystemSettingKeys.ProjectNumberPrefix,
            SystemSettingKeys.EquipmentNumberPrefix,
            SystemSettingKeys.ServiceNumberPrefix,
        };
        var baselineSettings = (await connection.QueryAsync<SettingState>(
            """
            SELECT setting_key AS `Key`,setting_value AS Value,version AS Version,
                   updated_at_utc AS UpdatedAtUtc,
                   updated_by_user_id AS UpdatedByUserId
            FROM system_settings
            WHERE setting_key IN @Keys;
            """,
            new { Keys = mutableSettingKeys })).ToDictionary(
                item => item.Key,
                StringComparer.Ordinal);
        var marker = $"Phase10-{Guid.NewGuid():N}"[..24];
        LookupItemDetails? lookup = null;
        CustomerDetails? customer = null;

        try
        {
            var administration = await settingsService.GetAdministrationAsync(
                CancellationToken.None);
            Assert.Equal(2, administration.Dictionaries.Count);
            Assert.Equal(15, administration.Settings.Count);

            lookup = await settingsService.CreateLookupItemAsync(
                new CreateLookupItemCommand(
                    LookupDictionaryCodes.CustomerIndustry,
                    marker,
                    $"行业 {marker}",
                    "真实 MySQL 验证",
                    15),
                actorUserId,
                CancellationToken.None);
            lookup = await settingsService.UpdateLookupItemAsync(
                lookup.Id,
                new UpdateLookupItemCommand(
                    $"更新 {marker}",
                    "停用验证",
                    16,
                    false,
                    lookup.Version),
                actorUserId,
                CancellationToken.None);
            Assert.False(lookup.IsActive);

            var settingValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SystemSettingKeys.DefaultPageSize] = "50",
                [SystemSettingKeys.CustomerNumberPrefix] = "C10",
                [SystemSettingKeys.OpportunityNumberPrefix] = "O10",
                [SystemSettingKeys.ProjectNumberPrefix] = "P10",
                [SystemSettingKeys.EquipmentNumberPrefix] = "E10",
                [SystemSettingKeys.ServiceNumberPrefix] = "S10",
            };
            foreach (var pair in settingValues)
            {
                var setting = administration.Settings.Single(
                    item => item.Key == pair.Key);
                _ = await settingsService.UpdateSystemSettingAsync(
                    setting.Key,
                    new UpdateSystemSettingCommand(pair.Value, setting.Version),
                    actorUserId,
                    CancellationToken.None);
            }

            var runtime = await settingsService.GetRuntimeAsync(
                CancellationToken.None);
            Assert.Equal(50, runtime.DefaultPageSize);
            Assert.Equal("C10", runtime.NumberPrefixes.Customer);
            Assert.Equal("O10", runtime.NumberPrefixes.Opportunity);
            Assert.Equal("P10", runtime.NumberPrefixes.Project);
            Assert.Equal("E10", runtime.NumberPrefixes.Equipment);
            Assert.Equal("S10", runtime.NumberPrefixes.Service);
            Assert.DoesNotContain(
                runtime.Lookups[LookupDictionaryCodes.CustomerIndustry],
                item => item.Id == lookup.Id);

            var customerService = new CustomerService(
                new MySqlCustomerRepository(factory, settingsService),
                new FixedTimeProvider(NowUtc));
            customer = await customerService.CreateAsync(
                new CreateCustomerCommand(
                    $"客户 {marker}",
                    null,
                    marker,
                    null,
                    null,
                    null,
                    null,
                    "Phase10 configured prefix verification"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("C10-2026-", customer.Code, StringComparison.Ordinal);

            var opportunityService = new OpportunityService(
                new MySqlOpportunityRepository(factory, settingsService),
                new FixedTimeProvider(NowUtc));
            var opportunity = await opportunityService.CreateAsync(
                new CreateOpportunityCommand(
                    customer.Id,
                    $"商机 {marker}",
                    OpportunityStage.Lead,
                    100000m,
                    10,
                    NowUtc.Date.AddMonths(1),
                    "Phase10 configured prefix verification"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("O10-2026-", opportunity.Code, StringComparison.Ordinal);

            var projectService = new ProjectService(
                new MySqlProjectRepository(factory, settingsService),
                new FixedTimeProvider(NowUtc));
            var project = await projectService.CreateAsync(
                new CreateProjectCommand(
                    customer.Id,
                    $"项目 {marker}",
                    100000m,
                    0,
                    "Phase10 configured prefix verification",
                    NowUtc.Date,
                    NowUtc.Date.AddMonths(2)),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("P10-2026-", project.Code, StringComparison.Ordinal);

            var equipmentService = new EquipmentService(
                new MySqlEquipmentRepository(factory, settingsService),
                new FixedTimeProvider(NowUtc));
            var equipment = await equipmentService.CreateAsync(
                new CreateEquipmentCommand(
                    customer.Id,
                    project.Id,
                    $"设备 {marker}",
                    EquipmentCategory.PLC,
                    "Test",
                    "Phase10",
                    marker,
                    null,
                    NowUtc.Date,
                    "Phase10 configured prefix verification"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("E10-2026-", equipment.Code, StringComparison.Ordinal);

            var serviceTicketService = new ServiceTicketService(
                new MySqlServiceTicketRepository(factory, settingsService),
                new FixedTimeProvider(NowUtc));
            var ticket = await serviceTicketService.CreateAsync(
                new CreateServiceTicketCommand(
                    customer.Id,
                    project.Id,
                    equipment.Id,
                    $"服务 {marker}",
                    "Phase10 configured prefix verification",
                    ServiceTicketPriority.P3,
                    NowUtc,
                    null),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith("S10-2026-", ticket.Code, StringComparison.Ordinal);

            var businessCodes = new[]
            {
                customer.Code,
                opportunity.Code,
                project.Code,
                equipment.Code,
                ticket.Code,
            };

            var auditCount = await connection.QuerySingleAsync<int>(
                """
                SELECT COUNT(*)
                FROM audit_logs
                WHERE id>@AuditBaseline
                  AND ((entity_type='LookupItem' AND entity_id=@LookupId)
                    OR (entity_type='SystemSetting'
                        AND entity_code IN @SettingKeys)
                    OR entity_code IN @BusinessCodes);
                """,
                new
                {
                    AuditBaseline = auditBaseline,
                    LookupId = lookup.Id,
                    SettingKeys = mutableSettingKeys,
                    BusinessCodes = businessCodes,
                });
            Assert.Equal(13, auditCount);
        }
        finally
        {
            if (customer is not null)
            {
                await connection.ExecuteAsync(
                    """
                    DELETE FROM audit_logs
                    WHERE id>@AuditBaseline
                      AND (entity_code IN (SELECT service_code FROM service_tickets WHERE customer_id=@CustomerId)
                        OR entity_code IN (SELECT equipment_code FROM equipment WHERE customer_id=@CustomerId)
                        OR entity_code IN (SELECT project_code FROM projects WHERE customer_id=@CustomerId)
                        OR entity_code IN (SELECT opportunity_code FROM opportunities WHERE customer_id=@CustomerId)
                        OR entity_code=@CustomerCode);
                    DELETE r FROM service_records r INNER JOIN service_tickets s ON s.id=r.service_ticket_id WHERE s.customer_id=@CustomerId;
                    DELETE FROM service_tickets WHERE customer_id=@CustomerId;
                    DELETE ec FROM equipment_components ec INNER JOIN equipment e ON e.id=ec.equipment_id WHERE e.customer_id=@CustomerId;
                    DELETE ep FROM equipment_parameters ep INNER JOIN equipment e ON e.id=ep.equipment_id WHERE e.customer_id=@CustomerId;
                    DELETE ev FROM equipment_versions ev INNER JOIN equipment e ON e.id=ev.equipment_id WHERE e.customer_id=@CustomerId;
                    DELETE FROM equipment WHERE customer_id=@CustomerId;
                    DELETE pm FROM project_members pm INNER JOIN projects p ON p.id=pm.project_id WHERE p.customer_id=@CustomerId;
                    DELETE mm FROM project_milestones mm INNER JOIN projects p ON p.id=mm.project_id WHERE p.customer_id=@CustomerId;
                    DELETE FROM projects WHERE customer_id=@CustomerId;
                    DELETE FROM opportunities WHERE customer_id=@CustomerId;
                    DELETE FROM customers WHERE id=@CustomerId;
                    """,
                    new
                    {
                        AuditBaseline = auditBaseline,
                        CustomerId = customer.Id,
                        CustomerCode = customer.Code,
                    });
            }

            if (lookup is not null)
            {
                await connection.ExecuteAsync(
                    "DELETE FROM audit_logs WHERE id>@AuditBaseline AND entity_type='LookupItem' AND entity_id=@Id; DELETE FROM lookup_items WHERE id=@Id;",
                    new { AuditBaseline = auditBaseline, lookup.Id });
            }

            foreach (var baseline in baselineSettings.Values)
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE system_settings
                    SET setting_value=@Value,version=@Version,
                        updated_at_utc=@UpdatedAtUtc,
                        updated_by_user_id=@UpdatedByUserId
                    WHERE setting_key=@Key;
                    """,
                    baseline);
            }

            await connection.ExecuteAsync(
                """
                DELETE FROM audit_logs
                WHERE id>@AuditBaseline AND entity_type='SystemSetting'
                  AND entity_code IN @SettingKeys;
                """,
                new
                {
                    AuditBaseline = auditBaseline,
                    SettingKeys = mutableSettingKeys,
                });

            Assert.Equal(
                0,
                await connection.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM customers WHERE notes='Phase10 configured prefix verification';"));
            Assert.Equal(
                0,
                await connection.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM lookup_items WHERE item_value=@Marker;",
                    new { Marker = marker }));
        }
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }

    private sealed class SettingState
    {
        public required string Key { get; init; }
        public required string Value { get; init; }
        public ulong Version { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public ulong? UpdatedByUserId { get; init; }
    }
}
