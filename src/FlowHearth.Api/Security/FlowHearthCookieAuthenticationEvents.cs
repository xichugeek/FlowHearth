using System.Globalization;
using System.Security.Claims;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlowHearth.Api.Security;

public sealed class FlowHearthCookieAuthenticationEvents(
    IAuthenticationRepository repository) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var securityVersionValue = context.Principal?
            .FindFirstValue(SecurityClaimTypes.SecurityVersion);
        if (!ulong.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId)
            || !ulong.TryParse(
                securityVersionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var securityVersion))
        {
            await RejectAsync(context);
            return;
        }

        var session = await repository.GetSecuritySessionAsync(
            userId,
            context.HttpContext.RequestAborted);
        if (session is null
            || !session.IsActive
            || session.SecurityVersion != securityVersion)
        {
            await RejectAsync(context);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
