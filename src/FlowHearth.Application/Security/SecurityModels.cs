namespace FlowHearth.Application.Security;

public sealed class UserAuthenticationRecord
{
    public ulong Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public string? Email { get; init; }

    public required string PasswordHash { get; init; }

    public bool IsActive { get; init; }

    public uint FailedLoginCount { get; init; }

    public DateTime? LockoutEndUtc { get; init; }

    public ulong SecurityVersion { get; init; }
}

public sealed record AuthenticatedUser(
    ulong Id,
    string Username,
    string DisplayName,
    string? Email,
    ulong SecurityVersion,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record SecuritySession(
    ulong UserId,
    bool IsActive,
    ulong SecurityVersion);

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record LoginResult(
    LoginStatus Status,
    AuthenticatedUser? User = null,
    DateTime? LockoutEndUtc = null);

public enum PasswordVerificationStatus
{
    Failed,
    Succeeded,
    SucceededRehashNeeded,
}
