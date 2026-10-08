using FlowHearth.Application.Common;

namespace FlowHearth.Application.Security;

public sealed class SecurityAdministrationService(
    ISecurityAdministrationRepository repository,
    IPasswordHashService passwordHashService,
    TimeProvider timeProvider) : ISecurityAdministrationService
{
    public Task<PagedResult<UserSummary>> ListUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For(
                "pageSize",
                "每页数量必须为 1 至 100。");
        }

        var trimmedSearch = search?.Trim();
        if (trimmedSearch?.Length > 100)
        {
            throw FlowHearthValidationException.For(
                "search",
                "搜索词不能超过 100 个字符。");
        }

        return repository.ListUsersAsync(
            page,
            pageSize,
            trimmedSearch,
            cancellationToken);
    }

    public async Task<UserDetails> GetUserAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        return await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new NotFoundException("用户不存在。");
    }

    public async Task<UserDetails> CreateUserAsync(
        CreateUserCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var username = SecurityText.RequireText(
            command.Username,
            "username",
            "用户名",
            64);
        var normalizedUsername = SecurityText.NormalizeUsername(username);
        var displayName = SecurityText.RequireText(
            command.DisplayName,
            "displayName",
            "显示名称",
            100);
        var email = SecurityText.NormalizeEmail(command.Email);
        var roleIds = ValidateIds(command.RoleIds, "roleIds", "至少选择一个角色。");
        PasswordPolicy.Validate(command.Password);
        var scope = await GetScopeAsync(actorUserId, cancellationToken);
        EnsureCanGrantRoles(scope, roleIds);

        return await repository.CreateUserAsync(
            new CreateUserData(
                username,
                normalizedUsername,
                displayName,
                email.Email,
                email.NormalizedEmail,
                passwordHashService.Hash(command.Password),
                roleIds,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    public async Task<UserDetails> UpdateUserAsync(
        ulong userId,
        UpdateUserCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (userId == actorUserId && !command.IsActive)
        {
            throw FlowHearthValidationException.For(
                "isActive",
                "不能停用当前登录用户。");
        }

        var existing = await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new NotFoundException("用户不存在。");
        if (existing.Version != command.Version)
        {
            throw new ConflictException("用户已被其他操作修改，请刷新后重试。");
        }

        var displayName = SecurityText.RequireText(
            command.DisplayName,
            "displayName",
            "显示名称",
            100);
        var email = SecurityText.NormalizeEmail(command.Email);
        var roleIds = ValidateIds(command.RoleIds, "roleIds", "至少选择一个角色。");
        var scope = await GetScopeAsync(actorUserId, cancellationToken);
        EnsureCanManageUser(scope, existing);
        EnsureCanGrantRoles(scope, roleIds);
        var administratorRole = scope.Roles.SingleOrDefault(
            role => string.Equals(
                role.Code,
                "administrator",
                StringComparison.Ordinal));
        if (userId == actorUserId
            && administratorRole is not null
            && existing.Roles.Any(role => role.Id == administratorRole.Id)
            && !roleIds.Contains(administratorRole.Id))
        {
            throw FlowHearthValidationException.For(
                "roleIds",
                "不能移除当前登录用户的 Administrator 角色。");
        }

        var updated = await repository.UpdateUserAsync(
            userId,
            new UpdateUserData(
                displayName,
                email.Email,
                email.NormalizedEmail,
                command.IsActive,
                roleIds,
                command.Version,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);

        return updated
            ?? throw new ConflictException("用户已被其他操作修改，请刷新后重试。");
    }

    public async Task ResetPasswordAsync(
        ulong userId,
        string newPassword,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new NotFoundException("用户不存在。");
        var scope = await GetScopeAsync(actorUserId, cancellationToken);
        EnsureCanManageUser(scope, existing);
        PasswordPolicy.Validate(newPassword, "newPassword");
        await repository.ResetPasswordAsync(
            userId,
            passwordHashService.Hash(newPassword),
            actorUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    public Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
        CancellationToken cancellationToken)
    {
        return repository.ListRolesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
        CancellationToken cancellationToken)
    {
        return repository.ListPermissionsAsync(cancellationToken);
    }

    public async Task<RoleDetails> CreateRoleAsync(
        CreateRoleCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var code = SecurityText.NormalizeRoleCode(command.Code);
        var name = SecurityText.RequireText(command.Name, "name", "角色名称", 100);
        var description = SecurityText.OptionalText(
            command.Description,
            "description",
            "角色说明",
            500);
        var permissionIds = ValidateIds(
            command.PermissionIds,
            "permissionIds",
            "至少选择一个权限。");
        var scope = await GetScopeAsync(actorUserId, cancellationToken);
        EnsureCanGrantPermissions(scope, permissionIds);

        return await repository.CreateRoleAsync(
            new CreateRoleData(
                code,
                name,
                description,
                permissionIds,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    public async Task<RoleDetails> UpdateRoleAsync(
        ulong roleId,
        UpdateRoleCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = (await repository.ListRolesAsync(cancellationToken))
            .SingleOrDefault(role => role.Id == roleId)
            ?? throw new NotFoundException("角色不存在。");
        if (string.Equals(existing.Code, "administrator", StringComparison.Ordinal))
        {
            throw FlowHearthValidationException.For(
                "roleId",
                "Administrator 角色的权限不可修改。");
        }

        if (existing.Version != command.Version)
        {
            throw new ConflictException("角色已被其他操作修改，请刷新后重试。");
        }

        var name = SecurityText.RequireText(command.Name, "name", "角色名称", 100);
        var description = SecurityText.OptionalText(
            command.Description,
            "description",
            "角色说明",
            500);
        var permissionIds = ValidateIds(
            command.PermissionIds,
            "permissionIds",
            "至少选择一个权限。");
        var scope = await GetScopeAsync(actorUserId, cancellationToken);
        EnsureCanManageRole(scope, existing);
        EnsureCanGrantPermissions(scope, permissionIds);
        if (!command.IsActive
            && scope.Actor.Roles.Any(role => role.Id == existing.Id))
        {
            throw FlowHearthValidationException.For(
                "isActive",
                "不能停用当前登录用户正在使用的角色。");
        }

        var updated = await repository.UpdateRoleAsync(
            roleId,
            new UpdateRoleData(
                name,
                description,
                command.IsActive,
                permissionIds,
                command.Version,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);

        return updated
            ?? throw new ConflictException("角色已被其他操作修改，请刷新后重试。");
    }

    private static ulong[] ValidateIds(
        IReadOnlyCollection<ulong>? values,
        string field,
        string emptyMessage)
    {
        var distinctValues = values?
            .Where(value => value > 0)
            .Distinct()
            .ToArray()
            ?? [];
        if (distinctValues.Length == 0)
        {
            throw FlowHearthValidationException.For(field, emptyMessage);
        }

        return distinctValues;
    }

    private async Task<SecurityAdministrationScope> GetScopeAsync(
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await repository.GetUserAsync(actorUserId, cancellationToken)
            ?? throw new ForbiddenException("当前用户已不可用。");
        var roles = await repository.ListRolesAsync(cancellationToken);
        var permissions = await repository.ListPermissionsAsync(cancellationToken);
        var actorRoleIds = actor.Roles.Select(role => role.Id).ToHashSet();
        var activeActorRoles = roles
            .Where(role => role.IsActive && actorRoleIds.Contains(role.Id))
            .ToArray();
        return new SecurityAdministrationScope(
            actor,
            roles,
            permissions,
            activeActorRoles.Any(role => string.Equals(
                role.Code,
                "administrator",
                StringComparison.Ordinal)),
            activeActorRoles
                .SelectMany(role => role.Permissions)
                .Select(permission => permission.Id)
                .ToHashSet());
    }

    private static void EnsureCanManageUser(
        SecurityAdministrationScope scope,
        UserDetails target)
    {
        if (scope.IsAdministrator)
        {
            return;
        }

        var targetRoleIds = target.Roles.Select(role => role.Id).ToHashSet();
        var targetRoles = scope.Roles
            .Where(role => role.IsActive && targetRoleIds.Contains(role.Id))
            .ToArray();
        if (targetRoles.Any(role => string.Equals(
                role.Code,
                "administrator",
                StringComparison.Ordinal))
            || targetRoles
                .SelectMany(role => role.Permissions)
                .Any(permission => !scope.PermissionIds.Contains(permission.Id)))
        {
            throw new ForbiddenException("不能管理权限高于当前用户的账户。");
        }
    }

    private static void EnsureCanGrantRoles(
        SecurityAdministrationScope scope,
        ulong[] roleIds)
    {
        var requested = scope.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToArray();
        if (requested.Length != roleIds.Length || requested.Any(role => !role.IsActive))
        {
            throw FlowHearthValidationException.For("roleIds", "包含不存在或已停用的角色。");
        }

        if (scope.IsAdministrator)
        {
            return;
        }

        if (requested.Any(role => string.Equals(
                role.Code,
                "administrator",
                StringComparison.Ordinal))
            || requested
                .SelectMany(role => role.Permissions)
                .Any(permission => !scope.PermissionIds.Contains(permission.Id)))
        {
            throw new ForbiddenException("不能授予当前用户不具备的角色权限。");
        }
    }

    private static void EnsureCanManageRole(
        SecurityAdministrationScope scope,
        RoleDetails target)
    {
        if (!scope.IsAdministrator
            && target.Permissions.Any(
                permission => !scope.PermissionIds.Contains(permission.Id)))
        {
            throw new ForbiddenException("不能管理权限高于当前用户的角色。");
        }
    }

    private static void EnsureCanGrantPermissions(
        SecurityAdministrationScope scope,
        ulong[] permissionIds)
    {
        var knownIds = scope.Permissions.Select(permission => permission.Id).ToHashSet();
        if (permissionIds.Any(permissionId => !knownIds.Contains(permissionId)))
        {
            throw FlowHearthValidationException.For("permissionIds", "包含不存在的权限。");
        }

        if (!scope.IsAdministrator
            && permissionIds.Any(permissionId => !scope.PermissionIds.Contains(permissionId)))
        {
            throw new ForbiddenException("不能授予当前用户不具备的权限。");
        }
    }

    private sealed record SecurityAdministrationScope(
        UserDetails Actor,
        IReadOnlyList<RoleDetails> Roles,
        IReadOnlyList<PermissionDetails> Permissions,
        bool IsAdministrator,
        IReadOnlySet<ulong> PermissionIds);
}
