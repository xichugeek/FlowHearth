using FlowHearth.Api.Security;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    FlowHearth.Application.Security.IAuthenticationService authenticationService,
    IAntiforgery antiforgery,
    IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrEmpty(tokens.RequestToken))
        {
            throw new InvalidOperationException("Could not create an antiforgery token.");
        }

        Response.Cookies.Append(
            "XSRF-TOKEN",
            tokens.RequestToken,
            new CookieOptions
            {
                HttpOnly = false,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Testing"),
                Path = "/",
            });
        return new CsrfTokenResponse(tokens.RequestToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthenticatedUser>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(
            request.Username,
            request.Password,
            cancellationToken);
        if (result.Status == LoginStatus.LockedOut)
        {
            return Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "账户暂时锁定",
                detail: "登录失败次数过多，请在锁定期结束后重试.",
                extensions: new Dictionary<string, object?>
                {
                    ["lockoutEndUtc"] = result.LockoutEndUtc,
                });
        }

        if (result.Status != LoginStatus.Succeeded || result.User is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "登录失败",
                detail: "用户名或密码错误，或账户已停用。");
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SecurityPrincipalFactory.Create(result.User),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = false,
            });
        return Ok(result.User);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Path = "/" });
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthenticatedUser>> GetCurrentUser(
        CancellationToken cancellationToken)
    {
        var user = await authenticationService.GetCurrentUserAsync(
            User.GetRequiredUserId(),
            cancellationToken);
        if (user is null)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized();
        }

        return Ok(user);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<AuthenticatedUser>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        await authenticationService.ChangePasswordAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        var user = await authenticationService.GetCurrentUserAsync(
            userId,
            cancellationToken)
            ?? throw new InvalidOperationException("Updated user could not be loaded.");
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SecurityPrincipalFactory.Create(user),
            new AuthenticationProperties { AllowRefresh = true, IsPersistent = false });
        return Ok(user);
    }

    public sealed record LoginRequest(string Username, string Password);

    public sealed record ChangePasswordRequest(
        string CurrentPassword,
        string NewPassword);

    public sealed record CsrfTokenResponse(string Token);
}
