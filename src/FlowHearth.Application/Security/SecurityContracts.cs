using FlowHearth.Application.Common;

namespace FlowHearth.Application.Security;

public interface IPasswordHashService
{
    string Hash(string password);

    PasswordVerificationStatus Verify(string passwordHash, string password);
}

public interface IAuthenticationRepository
{
    Task<UserAuthenticationRecord?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken);

    Task<UserAuthenticationRecord?> FindByIdAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task<DateTime?> RecordFailedLoginAsync(
        ulong userId,
        DateTime nowUtc,
        int maximumAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken);

    Task<AuthenticatedUser?> RecordSuccessfulLoginAsync(
        ulong userId,
        string? replacementPasswordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task<SecuritySession?> GetSecuritySessionAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task UpdatePasswordAsync(
        ulong userId,
        string passwordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public interface IAdministratorBootstrapRepository
{
    Task<bool> CreateFirstAdministratorAsync(
        BootstrapAdministratorData administrator,
        CancellationToken cancellationToken);
}

public interface IAdministratorBootstrapService
{
    Task<bool> BootstrapAsync(
        string username,
        string displayName,
        string? email,
        string password,
        CancellationToken cancellationToken);
}

public interface ISecurityAdministrationRepository
{
    Task<PagedResult<UserSummary>> ListUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);

    Task<UserDetails?> GetUserAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task<UserDetails> CreateUserAsync(
        CreateUserData user,
        CancellationToken cancellationToken);

    Task<UserDetails?> UpdateUserAsync(
        ulong userId,
        UpdateUserData user,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        ulong userId,
        string passwordHash,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
        CancellationToken cancellationToken);

    Task<RoleDetails> CreateRoleAsync(
        CreateRoleData role,
        CancellationToken cancellationToken);

    Task<RoleDetails?> UpdateRoleAsync(
        ulong roleId,
        UpdateRoleData role,
        CancellationToken cancellationToken);
}

public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken);

    Task<AuthenticatedUser?> GetCurrentUserAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task ChangePasswordAsync(
        ulong userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}

public interface ISecurityAdministrationService
{
    Task<PagedResult<UserSummary>> ListUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);

    Task<UserDetails> GetUserAsync(
        ulong userId,
        CancellationToken cancellationToken);

    Task<UserDetails> CreateUserAsync(
        CreateUserCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<UserDetails> UpdateUserAsync(
        ulong userId,
        UpdateUserCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        ulong userId,
        string newPassword,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDetails>> ListRolesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionDetails>> ListPermissionsAsync(
        CancellationToken cancellationToken);

    Task<RoleDetails> CreateRoleAsync(
        CreateRoleCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<RoleDetails> UpdateRoleAsync(
        ulong roleId,
        UpdateRoleCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
