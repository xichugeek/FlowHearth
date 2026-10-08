using FlowHearth.Application.Common;
using FlowHearth.Application.Security;

namespace FlowHearth.UnitTests;

public sealed class SecurityAdministrationServiceTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DelegatedManagerCanCreateUserOnlyWithSubsetRole()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var created = await service.CreateUserAsync(
            new CreateUserCommand(
                "new.user",
                "New User",
                null,
                "Strong!Password1",
                [FakeRepository.ViewerRoleId]),
            FakeRepository.ManagerUserId,
            CancellationToken.None);

        Assert.Equal(FakeRepository.CreatedUserId, created.Id);
        Assert.NotNull(repository.CreatedUser);
        Assert.Equal([FakeRepository.ViewerRoleId], repository.CreatedUser.RoleIds);
        Assert.Equal("hashed-password", repository.CreatedUser.PasswordHash);
    }

    [Fact]
    public async Task DelegatedManagerCannotAssignAdministratorOrSuperiorRole()
    {
        var service = CreateService(new FakeRepository());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateUserAsync(
            CommandWithRole(FakeRepository.AdministratorRoleId),
            FakeRepository.ManagerUserId,
            CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateUserAsync(
            CommandWithRole(FakeRepository.SuperiorRoleId),
            FakeRepository.ManagerUserId,
            CancellationToken.None));
    }

    [Fact]
    public async Task DelegatedManagerCannotManageSuperiorAccountOrResetItsPassword()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateUserAsync(
            FakeRepository.SuperiorUserId,
            new UpdateUserCommand(
                "Superior",
                null,
                true,
                [FakeRepository.ViewerRoleId],
                1),
            FakeRepository.ManagerUserId,
            CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ResetPasswordAsync(
            FakeRepository.AdministratorUserId,
            "Another!Password2",
            FakeRepository.ManagerUserId,
            CancellationToken.None));

        Assert.False(repository.PasswordWasReset);
    }

    [Fact]
    public async Task DelegatedRoleManagerCannotGrantPermissionItDoesNotHave()
    {
        var service = CreateService(new FakeRepository());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateRoleAsync(
            new CreateRoleCommand(
                "escalated",
                "Escalated",
                null,
                [FakeRepository.SettingsManagePermissionId]),
            FakeRepository.ManagerUserId,
            CancellationToken.None));
    }

    [Fact]
    public async Task AdministratorCanDelegateKnownRoleAndPermission()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        _ = await service.CreateUserAsync(
            CommandWithRole(FakeRepository.SuperiorRoleId),
            FakeRepository.AdministratorUserId,
            CancellationToken.None);
        _ = await service.CreateRoleAsync(
            new CreateRoleCommand(
                "settings-admin",
                "Settings Admin",
                null,
                [FakeRepository.SettingsManagePermissionId]),
            FakeRepository.AdministratorUserId,
            CancellationToken.None);

        Assert.NotNull(repository.CreatedUser);
        Assert.NotNull(repository.CreatedRole);
    }

    [Fact]
    public async Task AdministratorCannotRemoveOwnAdministratorRole()
    {
        var service = CreateService(new FakeRepository());

        var exception = await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.UpdateUserAsync(
                FakeRepository.AdministratorUserId,
                new UpdateUserCommand(
                    "Administrator",
                    null,
                    true,
                    [FakeRepository.ViewerRoleId],
                    1),
                FakeRepository.AdministratorUserId,
                CancellationToken.None));

        Assert.Contains("roleIds", exception.Errors.Keys);
    }

    [Fact]
    public async Task ActorCannotDeactivateRoleUsedByCurrentSession()
    {
        var service = CreateService(new FakeRepository());

        var exception = await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.UpdateRoleAsync(
                FakeRepository.ManagerRoleId,
                new UpdateRoleCommand(
                    "Delegated Manager",
                    null,
                    false,
                    [
                        FakeRepository.UsersManagePermissionId,
                        FakeRepository.RolesManagePermissionId,
                        FakeRepository.CustomersViewPermissionId,
                    ],
                    1),
                FakeRepository.ManagerUserId,
                CancellationToken.None));

        Assert.Contains("isActive", exception.Errors.Keys);
    }

    private static CreateUserCommand CommandWithRole(ulong roleId) => new(
        "new.user",
        "New User",
        null,
        "Strong!Password1",
        [roleId]);

    private static SecurityAdministrationService CreateService(
        FakeRepository repository) => new(
            repository,
            new FakePasswordHashService(),
            new FixedTimeProvider(NowUtc));

    private sealed class FakeRepository : ISecurityAdministrationRepository
    {
        public const ulong AdministratorUserId = 1;
        public const ulong ManagerUserId = 2;
        public const ulong SuperiorUserId = 3;
        public const ulong CreatedUserId = 100;
        public const ulong AdministratorRoleId = 10;
        public const ulong ManagerRoleId = 11;
        public const ulong SuperiorRoleId = 12;
        public const ulong ViewerRoleId = 13;
        public const ulong UsersManagePermissionId = 21;
        public const ulong RolesManagePermissionId = 22;
        public const ulong CustomersViewPermissionId = 23;
        public const ulong SettingsManagePermissionId = 24;

        private static readonly PermissionReference UsersManage =
            new(UsersManagePermissionId, SecurityPermissions.SecurityUsersManage, "用户管理");
        private static readonly PermissionReference RolesManage =
            new(RolesManagePermissionId, SecurityPermissions.SecurityRolesManage, "角色管理");
        private static readonly PermissionReference CustomersView =
            new(CustomersViewPermissionId, SecurityPermissions.CustomersView, "客户查看");
        private static readonly PermissionReference SettingsManage =
            new(SettingsManagePermissionId, SecurityPermissions.SettingsManage, "设置管理");

        private static readonly RoleDetails AdministratorRole = new(
            AdministratorRoleId,
            "administrator",
            "Administrator",
            null,
            true,
            true,
            1,
            [UsersManage, RolesManage, CustomersView, SettingsManage]);
        private static readonly RoleDetails ManagerRole = new(
            ManagerRoleId,
            "delegated-manager",
            "Delegated Manager",
            null,
            false,
            true,
            1,
            [UsersManage, RolesManage, CustomersView]);
        private static readonly RoleDetails SuperiorRole = new(
            SuperiorRoleId,
            "superior",
            "Superior",
            null,
            false,
            true,
            1,
            [UsersManage, RolesManage, CustomersView, SettingsManage]);
        private static readonly RoleDetails ViewerRole = new(
            ViewerRoleId,
            "viewer",
            "Viewer",
            null,
            false,
            true,
            1,
            [CustomersView]);

        private static readonly IReadOnlyList<RoleDetails> Roles =
            [AdministratorRole, ManagerRole, SuperiorRole, ViewerRole];

        public CreateUserData? CreatedUser { get; private set; }

        public CreateRoleData? CreatedRole { get; private set; }

        public bool PasswordWasReset { get; private set; }

        public Task<PagedResult<UserSummary>> ListUsersAsync(
            int page,
            int pageSize,
            string? search,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserDetails?> GetUserAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            UserDetails? user = userId switch
            {
                AdministratorUserId => User(
                    AdministratorUserId,
                    "admin",
                    "Administrator",
                    AdministratorRole),
                ManagerUserId => User(
                    ManagerUserId,
                    "manager",
                    "Manager",
                    ManagerRole),
                SuperiorUserId => User(
                    SuperiorUserId,
                    "superior",
                    "Superior",
                    SuperiorRole),
                _ => null,
            };
            return Task.FromResult(user);
        }

        public Task<UserDetails> CreateUserAsync(
            CreateUserData user,
            CancellationToken cancellationToken)
        {
            CreatedUser = user;
            var role = Roles.Single(item => item.Id == user.RoleIds.Single());
            return Task.FromResult(User(
                CreatedUserId,
                user.Username,
                user.DisplayName,
                role));
        }

        public Task<UserDetails?> UpdateUserAsync(
            ulong userId,
            UpdateUserData user,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResetPasswordAsync(
            ulong userId,
            string passwordHash,
            ulong actorUserId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            PasswordWasReset = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
            CancellationToken cancellationToken) => Task.FromResult(Roles);

        public Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
            CancellationToken cancellationToken) => Task.FromResult<
                IReadOnlyList<PermissionDetails>>(
                [
                    Permission(UsersManage),
                    Permission(RolesManage),
                    Permission(CustomersView),
                    Permission(SettingsManage),
                ]);

        public Task<RoleDetails> CreateRoleAsync(
            CreateRoleData role,
            CancellationToken cancellationToken)
        {
            CreatedRole = role;
            return Task.FromResult(new RoleDetails(
                20,
                role.Code,
                role.Name,
                role.Description,
                false,
                true,
                1,
                []));
        }

        public Task<RoleDetails?> UpdateRoleAsync(
            ulong roleId,
            UpdateRoleData role,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        private static UserDetails User(
            ulong id,
            string username,
            string displayName,
            RoleDetails role) => new(
                id,
                username,
                displayName,
                null,
                true,
                null,
                null,
                1,
                [new RoleReference(role.Id, role.Code, role.Name)]);

        private static PermissionDetails Permission(PermissionReference permission) =>
            new(permission.Id, permission.Code, permission.Name, "Security", null);
    }

    private sealed class FakePasswordHashService : IPasswordHashService
    {
        public string Hash(string password) => "hashed-password";

        public PasswordVerificationStatus Verify(string passwordHash, string password) =>
            PasswordVerificationStatus.Succeeded;
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }
}
