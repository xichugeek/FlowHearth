using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Security;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlSecurityHardeningTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 4, 30, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "LocalDatabase")]
    public async Task DelegationAuditSecretsAndLargePagingAreHardened()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new MySqlDbConnectionFactory(connectionString);
        var repository = new MySqlSecurityRepository(factory);
        var passwordHasher = new AspNetPasswordHashService();
        var service = new SecurityAdministrationService(
            repository,
            passwordHasher,
            new FixedTimeProvider(NowUtc));
        var authenticationService = new AuthenticationService(
            repository,
            passwordHasher,
            new FixedTimeProvider(NowUtc.AddMinutes(1)));
        await using var connection = await factory.OpenConnectionAsync(
            CancellationToken.None);
        await DeleteMarkedRowsAsync(connection);
        var administratorUserId = await connection.QuerySingleAsync<ulong>(
            """
            SELECT u.id
            FROM users u
            INNER JOIN user_roles ur ON ur.user_id=u.id
            INNER JOIN roles r ON r.id=ur.role_id
            WHERE r.code='administrator' AND u.is_active=1
              AND u.deleted_at_utc IS NULL
            ORDER BY u.id LIMIT 1;
            """);
        var administratorRoleId = await connection.QuerySingleAsync<ulong>(
            "SELECT id FROM roles WHERE code='administrator';");
        var permissions = (await connection.QueryAsync<PermissionIdRow>(
            """
            SELECT id AS Id,code AS Code
            FROM permissions
            WHERE code IN @Codes;
            """,
            new
            {
                Codes = new[]
                {
                    SecurityPermissions.SecurityUsersManage,
                    SecurityPermissions.SecurityRolesManage,
                    SecurityPermissions.CustomersView,
                    SecurityPermissions.SettingsManage,
                },
            })).ToDictionary(item => item.Code, item => item.Id, StringComparer.Ordinal);
        var auditBaseline = await connection.QuerySingleAsync<ulong>(
            "SELECT COALESCE(MAX(id),0) FROM audit_logs;");
        var marker = $"p11-{Guid.NewGuid():N}"[..16];
        var managerRoleCode = $"{marker}-manager";
        var limitedRoleCode = $"{marker}-limited";
        var managerUsername = $"{marker}-manager";
        var limitedUsername = $"{marker}-limited";
        RoleDetails? managerRole = null;
        RoleDetails? limitedRole = null;
        UserDetails? manager = null;
        UserDetails? limited = null;
        const string originalPassword = "Strong!Password1";
        const string resetPassword = "Another!Password2";

        try
        {
            managerRole = await service.CreateRoleAsync(
                new CreateRoleCommand(
                    managerRoleCode,
                    "Phase11 delegated manager",
                    null,
                    [
                        permissions[SecurityPermissions.SecurityUsersManage],
                        permissions[SecurityPermissions.SecurityRolesManage],
                        permissions[SecurityPermissions.CustomersView],
                    ]),
                administratorUserId,
                CancellationToken.None);
            limitedRole = await service.CreateRoleAsync(
                new CreateRoleCommand(
                    limitedRoleCode,
                    "Phase11 limited",
                    null,
                    [permissions[SecurityPermissions.CustomersView]]),
                administratorUserId,
                CancellationToken.None);
            manager = await service.CreateUserAsync(
                new CreateUserCommand(
                    managerUsername,
                    "Phase11 Manager",
                    null,
                    originalPassword,
                    [managerRole.Id]),
                administratorUserId,
                CancellationToken.None);

            await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateUserAsync(
                new CreateUserCommand(
                    $"{marker}-blocked",
                    "Blocked escalation",
                    null,
                    originalPassword,
                    [administratorRoleId]),
                manager.Id,
                CancellationToken.None));
            await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateRoleAsync(
                new CreateRoleCommand(
                    $"{marker}-blocked",
                    "Blocked permission escalation",
                    null,
                    [permissions[SecurityPermissions.SettingsManage]]),
                manager.Id,
                CancellationToken.None));

            limited = await service.CreateUserAsync(
                new CreateUserCommand(
                    limitedUsername,
                    "Phase11 Limited",
                    null,
                    originalPassword,
                    [limitedRole.Id]),
                manager.Id,
                CancellationToken.None);
            limited = await service.UpdateUserAsync(
                limited.Id,
                new UpdateUserCommand(
                    "Phase11 Limited Updated",
                    null,
                    true,
                    [limitedRole.Id],
                    limited.Version),
                manager.Id,
                CancellationToken.None);
            await service.ResetPasswordAsync(
                limited.Id,
                resetPassword,
                manager.Id,
                CancellationToken.None);
            await authenticationService.ChangePasswordAsync(
                limited.Id,
                resetPassword,
                "Final!Password3",
                CancellationToken.None);
            limitedRole = await service.UpdateRoleAsync(
                limitedRole.Id,
                new UpdateRoleCommand(
                    "Phase11 limited updated",
                    null,
                    true,
                    [permissions[SecurityPermissions.CustomersView]],
                    limitedRole.Version),
                administratorUserId,
                CancellationToken.None);

            var farPage = await repository.ListUsersAsync(
                int.MaxValue,
                100,
                marker,
                CancellationToken.None);
            Assert.Empty(farPage.Items);
            var literalWildcardSearch = await repository.ListUsersAsync(
                1,
                100,
                $"{marker}%",
                CancellationToken.None);
            Assert.Empty(literalWildcardSearch.Items);

            var audits = (await connection.QueryAsync<SecurityAuditRow>(
                """
                SELECT action AS Action,before_json AS BeforeJson,
                       after_json AS AfterJson
                FROM audit_logs
                WHERE id>@AuditBaseline AND entity_code IN @Codes
                ORDER BY id;
                """,
                new
                {
                    AuditBaseline = auditBaseline,
                    Codes = new[]
                    {
                        managerRoleCode,
                        limitedRoleCode,
                        managerUsername,
                        limitedUsername,
                    },
                })).ToArray();
            Assert.Equal(8, audits.Length);
            Assert.Contains(audits, item => item.Action == "security.user.password_reset");
            Assert.Contains(audits, item => item.Action == "security.user.password_changed");
            Assert.Contains(audits, item => item.Action == "security.user.updated");
            Assert.Contains(audits, item => item.Action == "security.role.updated");
            var serialized = string.Join(
                '\n',
                audits.Select(item => $"{item.BeforeJson}\n{item.AfterJson}"));
            Assert.DoesNotContain(originalPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(resetPassword, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("Final!Password3", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("password_hash", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hashed-password", serialized, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteMarkedRowsAsync(connection);
            var residue = await connection.QuerySingleAsync<int>(
                """
                SELECT
                  (SELECT COUNT(*) FROM users WHERE username LIKE 'p11-%') +
                  (SELECT COUNT(*) FROM roles WHERE code LIKE 'p11-%') +
                  (SELECT COUNT(*) FROM audit_logs
                   WHERE id>@AuditBaseline AND entity_code LIKE 'p11-%');
                """,
                new { AuditBaseline = auditBaseline });
            Assert.Equal(0, residue);
        }
    }

    private static Task<int> DeleteMarkedRowsAsync(
        System.Data.Common.DbConnection connection) => connection.ExecuteAsync(
            """
            DELETE FROM audit_logs WHERE entity_code LIKE 'p11-%';
            DELETE ur FROM user_roles ur INNER JOIN users u ON u.id=ur.user_id
            WHERE u.username LIKE 'p11-%';
            DELETE FROM users WHERE username LIKE 'p11-%';
            DELETE rp FROM role_permissions rp INNER JOIN roles r ON r.id=rp.role_id
            WHERE r.code LIKE 'p11-%';
            DELETE FROM roles WHERE code LIKE 'p11-%';
            """);

    private sealed class PermissionIdRow
    {
        public ulong Id { get; init; }

        public required string Code { get; init; }
    }

    private sealed class SecurityAuditRow
    {
        public required string Action { get; init; }

        public string? BeforeJson { get; init; }

        public string? AfterJson { get; init; }
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }
}
