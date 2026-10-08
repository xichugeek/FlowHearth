using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FlowHearth.Api.Health;
using FlowHearth.Api.Middleware;
using FlowHearth.Api.Security;
using FlowHearth.Application.Security;
using FlowHearth.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) =>
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

var dataProtectionKeysPath = builder.Configuration[
    "Security:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services
        .AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("FlowHearth");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["correlationId"] =
            context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".FlowHearth.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            || builder.Environment.IsEnvironment("Testing")
                ? CookieSecurePolicy.None
                : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.EventsType = typeof(FlowHearthCookieAuthenticationEvents);
    });
builder.Services.AddScoped<FlowHearthCookieAuthenticationEvents>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in SecurityPermissions.All)
    {
        options.AddPolicy(
            permission,
            policy => policy.RequireClaim(SecurityClaimTypes.Permission, permission));
    }
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".FlowHearth.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        || builder.Environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.None
            : CookieSecurePolicy.Always;
    options.HeaderName = "X-XSRF-TOKEN";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "login",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services.AddControllers(options =>
    options.Filters.Add(new AuthorizeFilter()))
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set(
            "CorrelationId",
            httpContext.TraceIdentifier);
        diagnosticContext.Set(
            "RequestHost",
            httpContext.Request.Host.Value);
    };
});
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ApiAntiforgeryMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    }).AllowAnonymous();
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteAsync,
    }).AllowAnonymous();

app.Run();

public partial class Program;
