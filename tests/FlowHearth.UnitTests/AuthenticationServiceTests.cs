using FlowHearth.Application.Security;

namespace FlowHearth.UnitTests;

public sealed class AuthenticationServiceTests
{
    private static readonly DateTime TestNowUtc =
        new(2026, 8, 29, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FifthFailedPasswordLocksAccountForFifteenMinutes()
    {
        var repository = new FakeAuthenticationRepository(CreateUser());
        var service = CreateService(repository);

        for (var attempt = 1; attempt < AuthenticationService.MaximumFailedAttempts; attempt++)
        {
            var result = await service.LoginAsync(
                "admin",
                "wrong-password",
                CancellationToken.None);
            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        var lockedResult = await service.LoginAsync(
            "admin",
            "wrong-password",
            CancellationToken.None);

        Assert.Equal(LoginStatus.LockedOut, lockedResult.Status);
        Assert.Equal(TestNowUtc.AddMinutes(15), lockedResult.LockoutEndUtc);
        Assert.Equal(AuthenticationService.MaximumFailedAttempts, repository.FailedAttempts);
    }

    [Fact]
    public async Task SuccessfulLoginResetsFailuresAndReturnsPermissions()
    {
        var repository = new FakeAuthenticationRepository(CreateUser());
        var service = CreateService(repository);

        var result = await service.LoginAsync(
            "ADMIN",
            FakePasswordHashService.ValidPassword,
            CancellationToken.None);

        Assert.Equal(LoginStatus.Succeeded, result.Status);
        Assert.NotNull(result.User);
        Assert.Contains(SecurityPermissions.SecurityUsersManage, result.User.Permissions);
        Assert.True(repository.SuccessRecorded);
    }

    [Fact]
    public async Task DisabledUserCannotAuthenticate()
    {
        var repository = new FakeAuthenticationRepository(
            CreateUser(isActive: false));
        var service = CreateService(repository);

        var result = await service.LoginAsync(
            "admin",
            FakePasswordHashService.ValidPassword,
            CancellationToken.None);

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.False(repository.SuccessRecorded);
        Assert.Equal(0, repository.FailedAttempts);
    }

    [Theory]
    [InlineData("Short1!")]
    [InlineData("alllowercase123!")]
    [InlineData("ALLUPPERCASE123!")]
    [InlineData("NoNumbersHere!")]
    [InlineData("NoSpecialChar123")]
    public void PasswordPolicyRejectsWeakPasswords(string password)
    {
        Assert.Throws<FlowHearth.Application.Common.FlowHearthValidationException>(
            () => PasswordPolicy.Validate(password));
    }

    private static AuthenticationService CreateService(
        FakeAuthenticationRepository repository)
    {
        return new AuthenticationService(
            repository,
            new FakePasswordHashService(),
            new FixedTimeProvider(TestNowUtc));
    }

    private static UserAuthenticationRecord CreateUser(bool isActive = true)
    {
        return new UserAuthenticationRecord
        {
            Id = 1,
            Username = "admin",
            DisplayName = "Administrator",
            PasswordHash = "valid-hash",
            IsActive = isActive,
            SecurityVersion = 1,
        };
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }

    private sealed class FakePasswordHashService : IPasswordHashService
    {
        public const string ValidPassword = "Strong!Password1";

        public string Hash(string password) => $"hash:{password}";

        public PasswordVerificationStatus Verify(string passwordHash, string password)
        {
            return password == ValidPassword
                ? PasswordVerificationStatus.Succeeded
                : PasswordVerificationStatus.Failed;
        }
    }

    private sealed class FakeAuthenticationRepository(UserAuthenticationRecord user)
        : IAuthenticationRepository
    {
        public int FailedAttempts { get; private set; }

        public bool SuccessRecorded { get; private set; }

        public Task<UserAuthenticationRecord?> FindByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<UserAuthenticationRecord?>(user);
        }

        public Task<UserAuthenticationRecord?> FindByIdAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<UserAuthenticationRecord?>(user);
        }

        public Task<DateTime?> RecordFailedLoginAsync(
            ulong userId,
            DateTime nowUtc,
            int maximumAttempts,
            TimeSpan lockoutDuration,
            CancellationToken cancellationToken)
        {
            FailedAttempts++;
            DateTime? lockout = FailedAttempts >= maximumAttempts
                ? nowUtc.Add(lockoutDuration)
                : null;
            return Task.FromResult(lockout);
        }

        public Task<AuthenticatedUser?> RecordSuccessfulLoginAsync(
            ulong userId,
            string? replacementPasswordHash,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            SuccessRecorded = true;
            return Task.FromResult<AuthenticatedUser?>(CreateAuthenticatedUser());
        }

        public Task<AuthenticatedUser?> GetAuthenticatedUserAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<AuthenticatedUser?>(CreateAuthenticatedUser());
        }

        public Task<SecuritySession?> GetSecuritySessionAsync(
            ulong userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<SecuritySession?>(new SecuritySession(userId, true, 1));
        }

        public Task UpdatePasswordAsync(
            ulong userId,
            string passwordHash,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static AuthenticatedUser CreateAuthenticatedUser()
        {
            return new AuthenticatedUser(
                1,
                "admin",
                "Administrator",
                null,
                1,
                ["administrator"],
                [SecurityPermissions.SecurityUsersManage]);
        }
    }
}
