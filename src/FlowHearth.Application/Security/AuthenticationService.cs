using FlowHearth.Application.Common;

namespace FlowHearth.Application.Security;

public sealed class AuthenticationService(
    IAuthenticationRepository repository,
    IPasswordHashService passwordHashService,
    TimeProvider timeProvider) : IAuthenticationService
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        string normalizedUsername;
        try
        {
            normalizedUsername = SecurityText.NormalizeUsername(username);
        }
        catch (FlowHearthValidationException)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var user = await repository.FindByNormalizedUsernameAsync(
            normalizedUsername,
            cancellationToken);
        if (user is null || !user.IsActive)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (user.LockoutEndUtc is { } lockoutEndUtc && lockoutEndUtc > nowUtc)
        {
            return new LoginResult(LoginStatus.LockedOut, LockoutEndUtc: lockoutEndUtc);
        }

        var verification = passwordHashService.Verify(user.PasswordHash, password);
        if (verification == PasswordVerificationStatus.Failed)
        {
            var newLockoutEndUtc = await repository.RecordFailedLoginAsync(
                user.Id,
                nowUtc,
                MaximumFailedAttempts,
                LockoutDuration,
                cancellationToken);
            return new LoginResult(
                newLockoutEndUtc > nowUtc
                    ? LoginStatus.LockedOut
                    : LoginStatus.InvalidCredentials,
                LockoutEndUtc: newLockoutEndUtc);
        }

        var replacementHash = verification == PasswordVerificationStatus.SucceededRehashNeeded
            ? passwordHashService.Hash(password)
            : null;
        var authenticatedUser = await repository.RecordSuccessfulLoginAsync(
            user.Id,
            replacementHash,
            nowUtc,
            cancellationToken);

        return authenticatedUser is null
            ? new LoginResult(LoginStatus.InvalidCredentials)
            : new LoginResult(LoginStatus.Succeeded, authenticatedUser);
    }

    public Task<AuthenticatedUser?> GetCurrentUserAsync(
        ulong userId,
        CancellationToken cancellationToken)
    {
        return repository.GetAuthenticatedUserAsync(userId, cancellationToken);
    }

    public async Task ChangePasswordAsync(
        ulong userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        PasswordPolicy.Validate(newPassword, "newPassword");
        var user = await repository.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("当前用户不存在。");

        if (!user.IsActive
            || passwordHashService.Verify(user.PasswordHash, currentPassword)
                == PasswordVerificationStatus.Failed)
        {
            throw FlowHearthValidationException.For(
                "currentPassword",
                "当前密码不正确。");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            throw FlowHearthValidationException.For(
                "newPassword",
                "新密码不能与当前密码相同。");
        }

        await repository.UpdatePasswordAsync(
            userId,
            passwordHashService.Hash(newPassword),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }
}
