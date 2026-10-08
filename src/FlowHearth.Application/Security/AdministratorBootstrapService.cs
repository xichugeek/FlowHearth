namespace FlowHearth.Application.Security;

public sealed class AdministratorBootstrapService(
    IAdministratorBootstrapRepository repository,
    IPasswordHashService passwordHashService,
    TimeProvider timeProvider) : IAdministratorBootstrapService
{
    public Task<bool> BootstrapAsync(
        string username,
        string displayName,
        string? email,
        string password,
        CancellationToken cancellationToken)
    {
        var trimmedUsername = SecurityText.RequireText(
            username,
            "username",
            "用户名",
            64);
        var normalizedUsername = SecurityText.NormalizeUsername(trimmedUsername);
        var trimmedDisplayName = SecurityText.RequireText(
            displayName,
            "displayName",
            "显示名称",
            100);
        var normalizedEmail = SecurityText.NormalizeEmail(email);
        PasswordPolicy.Validate(password);

        return repository.CreateFirstAdministratorAsync(
            new BootstrapAdministratorData(
                trimmedUsername,
                normalizedUsername,
                trimmedDisplayName,
                normalizedEmail.Email,
                normalizedEmail.NormalizedEmail,
                passwordHashService.Hash(password),
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }
}
