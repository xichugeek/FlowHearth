using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Identity;

namespace FlowHearth.Infrastructure.Security;

public sealed class AspNetPasswordHashService : IPasswordHashService
{
    private static readonly object PasswordOwner = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string password)
    {
        return _passwordHasher.HashPassword(PasswordOwner, password);
    }

    public PasswordVerificationStatus Verify(string passwordHash, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(
            PasswordOwner,
            passwordHash,
            password);

        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerificationStatus.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded =>
                PasswordVerificationStatus.SucceededRehashNeeded,
            _ => PasswordVerificationStatus.Failed,
        };
    }
}
