namespace FlowHearth.Application.Security;

public sealed record BootstrapAdministratorData(
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string? Email,
    string? NormalizedEmail,
    string PasswordHash,
    DateTime CreatedAtUtc);

public sealed record UserSummary(
    ulong Id,
    string Username,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTime? LastLoginAtUtc,
    DateTime? LockoutEndUtc,
    ulong Version,
    IReadOnlyList<RoleReference> Roles);

public sealed record UserDetails(
    ulong Id,
    string Username,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTime? LastLoginAtUtc,
    DateTime? LockoutEndUtc,
    ulong Version,
    IReadOnlyList<RoleReference> Roles);

public sealed record RoleReference(ulong Id, string Code, string Name);

public sealed record PermissionDetails(
    ulong Id,
    string Code,
    string Name,
    string Module,
    string? Description);

public sealed record RoleDetails(
    ulong Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    ulong Version,
    IReadOnlyList<PermissionReference> Permissions);

public sealed record PermissionReference(ulong Id, string Code, string Name);

public sealed record CreateUserCommand(
    string Username,
    string DisplayName,
    string? Email,
    string Password,
    IReadOnlyCollection<ulong> RoleIds);

public sealed record UpdateUserCommand(
    string DisplayName,
    string? Email,
    bool IsActive,
    IReadOnlyCollection<ulong> RoleIds,
    ulong Version);

public sealed record CreateRoleCommand(
    string Code,
    string Name,
    string? Description,
    IReadOnlyCollection<ulong> PermissionIds);

public sealed record UpdateRoleCommand(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyCollection<ulong> PermissionIds,
    ulong Version);

public sealed record CreateUserData(
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string? Email,
    string? NormalizedEmail,
    string PasswordHash,
    IReadOnlyCollection<ulong> RoleIds,
    ulong ActorUserId,
    DateTime CreatedAtUtc);

public sealed record UpdateUserData(
    string DisplayName,
    string? Email,
    string? NormalizedEmail,
    bool IsActive,
    IReadOnlyCollection<ulong> RoleIds,
    ulong Version,
    ulong ActorUserId,
    DateTime UpdatedAtUtc);

public sealed record CreateRoleData(
    string Code,
    string Name,
    string? Description,
    IReadOnlyCollection<ulong> PermissionIds,
    ulong ActorUserId,
    DateTime CreatedAtUtc);

public sealed record UpdateRoleData(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyCollection<ulong> PermissionIds,
    ulong Version,
    ulong ActorUserId,
    DateTime UpdatedAtUtc);
