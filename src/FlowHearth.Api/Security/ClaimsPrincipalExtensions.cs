using System.Globalization;
using System.Security.Claims;

namespace FlowHearth.Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool HasPermission(
        this ClaimsPrincipal principal,
        string permission) =>
        principal.HasClaim(SecurityClaimTypes.Permission, permission);

    public static ulong GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId == 0)
        {
            throw new InvalidOperationException(
                "The authenticated principal does not contain a valid user identifier.");
        }

        return userId;
    }
}
