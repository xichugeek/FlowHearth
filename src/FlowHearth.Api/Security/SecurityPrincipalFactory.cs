using System.Globalization;
using System.Security.Claims;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowHearth.Api.Security;

public static class SecurityPrincipalFactory
{
    public static ClaimsPrincipal Create(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new(
                SecurityClaimTypes.SecurityVersion,
                user.SecurityVersion.ToString(CultureInfo.InvariantCulture)),
        };
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Permissions.Select(permission =>
            new Claim(SecurityClaimTypes.Permission, permission)));

        return new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
