using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Domain.Customers;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlCustomerFlowTests
{
    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task ConcurrentCustomerCreationProducesUniqueBusinessCodes()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var connectionFactory = new MySqlDbConnectionFactory(connectionString);
        var repository = new MySqlCustomerRepository(connectionFactory);
        var service = new CustomerService(repository, TimeProvider.System);
        await using var connection = await connectionFactory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        var runId = Guid.NewGuid().ToString("N");
        try
        {
            var creates = Enumerable.Range(1, 8)
                .Select(index => service.CreateAsync(
                    new CreateCustomerCommand(
                        $"Phase3 Sequence {runId} {index}",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        "仅用于并发验证"),
                    actorUserId,
                    CancellationToken.None));
            var customers = await Task.WhenAll(creates);

            Assert.Equal(8, customers.Select(customer => customer.Code).Distinct().Count());
            Assert.All(
                customers,
                customer => Assert.StartsWith(
                    $"CU-{DateTime.UtcNow.Year:D4}-",
                    customer.Code));
        }
        finally
        {
            var searchPattern = $"Phase3 Sequence {runId} %";
            await connection.ExecuteAsync(
                """
                DELETE a
                FROM audit_logs AS a
                INNER JOIN customers AS c ON c.customer_code = a.entity_code
                WHERE c.name LIKE @SearchPattern
                  AND c.notes = '仅用于并发验证';
                DELETE FROM customers
                WHERE name LIKE @SearchPattern
                  AND notes = '仅用于并发验证';
                """,
                new { SearchPattern = searchPattern });
        }
    }

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task CustomerLifecyclePersistsContactsFollowUpsAuditAndConcurrency()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var connectionFactory = new MySqlDbConnectionFactory(connectionString);
        var repository = new MySqlCustomerRepository(connectionFactory);
        var service = new CustomerService(repository, TimeProvider.System);
        await using var setupConnection = await connectionFactory.OpenConnectionAsync(CancellationToken.None);
        var actorUserId = await setupConnection.QuerySingleAsync<ulong>(
            "SELECT id FROM users WHERE deleted_at_utc IS NULL ORDER BY id LIMIT 1;");
        await RemoveAbandonedTestCustomersAsync(setupConnection);
        CustomerDetails? created = null;
        try
        {
            created = await service.CreateAsync(
                new CreateCustomerCommand(
                    $"Phase3 MySQL {Guid.NewGuid():N}",
                    "真实数据库验证",
                    "Automation",
                    "0755-12345678",
                    "phase3@example.com",
                    "https://example.com",
                    "深圳",
                    "仅用于自动验证",
                    CustomerStatus.Prospect,
                    CustomerLevel.A,
                    "44",
                    "4403",
                    "440305"),
                actorUserId,
                CancellationToken.None);
            Assert.StartsWith($"CU-{DateTime.UtcNow.Year:D4}-", created.Code);
            Assert.Equal(CustomerStatus.Prospect, created.Status);
            Assert.Equal(CustomerLevel.A, created.Level);
            Assert.Equal("44", created.ProvinceCode);
            Assert.Equal("4403", created.CityCode);
            Assert.Equal("440305", created.DistrictCode);

            var firstContact = await service.CreateContactAsync(
                created.Id,
                new CreateContactCommand("联系人甲", null, null, "13800000001", null, null, null, true, null),
                actorUserId,
                CancellationToken.None);
            var secondContact = await service.CreateContactAsync(
                created.Id,
                new CreateContactCommand("联系人乙", null, null, "13800000002", null, null, null, true, null),
                actorUserId,
                CancellationToken.None);
            var afterContacts = await service.GetAsync(created.Id, CancellationToken.None);
            Assert.Equal(2, afterContacts.Contacts.Count);
            Assert.False(afterContacts.Contacts.Single(contact => contact.Id == firstContact.Id).IsPrimary);
            Assert.True(afterContacts.Contacts.Single(contact => contact.Id == secondContact.Id).IsPrimary);

            var contactSummary = await service.ListAsync(
                1,
                20,
                created.Code,
                "active",
                "Prospect",
                "A",
                "updatedAt",
                true,
                null,
                CancellationToken.None,
                provinceCode: "44",
                cityCode: "4403",
                districtCode: "440305");
            var listedCustomer = Assert.Single(contactSummary.Items);
            Assert.Equal(secondContact.Name, listedCustomer.PrimaryContactName);
            Assert.Equal(secondContact.Mobile, listedCustomer.PrimaryContactMethod);

            var occurredAtUtc = DateTime.UtcNow.AddMinutes(-10);
            var nextAtUtc = DateTime.UtcNow.AddDays(2);
            var followUp = await service.CreateFollowUpAsync(
                created.Id,
                new CreateCustomerFollowUpCommand(
                    secondContact.Id,
                    CustomerFollowUpMethod.Phone,
                    occurredAtUtc,
                    "确认需求",
                    "客户将在下周提供技术资料。",
                    nextAtUtc),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(secondContact.Id, followUp.ContactId);

            var due = await service.ListAsync(
                1,
                20,
                created.Code,
                "active",
                "Prospect",
                "A",
                "nextFollowUpAt",
                false,
                nextAtUtc.AddMinutes(1),
                CancellationToken.None);
            Assert.Contains(due.Items, customer => customer.Id == created.Id);

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.UpdateAsync(
                    created.Id,
                    new UpdateCustomerCommand(created.Name, null, null, null, null, null, null, null, 999),
                    actorUserId,
                    CancellationToken.None));

            var updated = await service.UpdateAsync(
                created.Id,
                new UpdateCustomerCommand(
                    created.Name + " 已更新",
                    created.ShortName,
                    created.Industry,
                    created.Phone,
                    created.Email,
                    created.Website,
                    created.Address,
                    created.Notes,
                    created.Version,
                    CustomerStatus.Active,
                    CustomerLevel.B,
                    created.ProvinceCode,
                    created.CityCode,
                    created.DistrictCode),
                actorUserId,
                CancellationToken.None);
            Assert.Equal(CustomerStatus.Active, updated.Status);
            Assert.Equal(CustomerLevel.B, updated.Level);

            var updatedClassification = await service.ListAsync(
                1,
                10,
                created.Code,
                "active",
                "Active",
                "B",
                "updatedAt",
                true,
                null,
                CancellationToken.None);
            Assert.Equal(created.Id, Assert.Single(updatedClassification.Items).Id);
            var archived = await service.SetArchivedAsync(
                created.Id,
                true,
                new CustomerVersionCommand(updated.Version),
                actorUserId,
                CancellationToken.None);
            Assert.True(archived.IsArchived);
            await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
                service.CreateContactAsync(
                    created.Id,
                    new CreateContactCommand("归档后联系人", null, null, null, null, null, null, false, null),
                    actorUserId,
                    CancellationToken.None));
            var restored = await service.SetArchivedAsync(
                created.Id,
                false,
                new CustomerVersionCommand(archived.Version),
                actorUserId,
                CancellationToken.None);
            Assert.False(restored.IsArchived);

            var auditCount = await setupConnection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_code = @Code;",
                new { created.Code });
            Assert.True(auditCount >= 6);
        }
        finally
        {
            if (created is not null)
            {
                await setupConnection.ExecuteAsync(
                    """
                    DELETE FROM audit_logs WHERE entity_code = @Code;
                    DELETE FROM customer_followups WHERE customer_id = @CustomerId;
                    DELETE FROM contacts WHERE customer_id = @CustomerId;
                    DELETE FROM customers WHERE id = @CustomerId;
                    """,
                    new { Code = created.Code, CustomerId = created.Id });
            }

            var residualCount = await setupConnection.QuerySingleAsync<int>(
                """
                SELECT COUNT(*)
                FROM customers
                WHERE name LIKE 'Phase3 MySQL %'
                  AND notes = '仅用于自动验证';
                """);
            Assert.Equal(0, residualCount);
        }
    }

    private static async Task RemoveAbandonedTestCustomersAsync(
        System.Data.Common.DbConnection connection)
    {
        await connection.ExecuteAsync(
            """
            DELETE a
            FROM audit_logs AS a
            INNER JOIN customers AS c ON c.customer_code = a.entity_code
            WHERE c.name LIKE 'Phase3 MySQL %'
              AND c.notes = '仅用于自动验证';

            DELETE f
            FROM customer_followups AS f
            INNER JOIN customers AS c ON c.id = f.customer_id
            WHERE c.name LIKE 'Phase3 MySQL %'
              AND c.notes = '仅用于自动验证';

            DELETE ct
            FROM contacts AS ct
            INNER JOIN customers AS c ON c.id = ct.customer_id
            WHERE c.name LIKE 'Phase3 MySQL %'
              AND c.notes = '仅用于自动验证';

            DELETE FROM customers
            WHERE name LIKE 'Phase3 MySQL %'
              AND notes = '仅用于自动验证';
            """);
    }
}
