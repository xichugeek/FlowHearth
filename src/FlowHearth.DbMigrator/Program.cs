using System.Diagnostics;
using System.Globalization;
using FlowHearth.Application.Security;
using FlowHearth.Infrastructure;
using FlowHearth.Infrastructure.Database.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

return await RunAsync(args);

static async Task<int> RunAsync(string[] commandLineArguments)
{
    var command = commandLineArguments.FirstOrDefault()?.Trim().ToLowerInvariant()
        ?? "status";

    if (command is "help" or "--help" or "-h")
    {
        WriteUsage();
        return 0;
    }

    if (command is not ("status" or "validate" or "migrate" or "bootstrap-admin"
        or "bootstrap-development-admin"))
    {
        Console.Error.WriteLine($"Unknown migration command: {command}");
        WriteUsage();
        return 2;
    }

    Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
                "{Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSerilog(Log.Logger, dispose: true);
    builder.Services.AddInfrastructure(builder.Configuration);

    using var host = builder.Build();

    try
    {
        if (command == "bootstrap-admin")
        {
            return await BootstrapAdministratorAsync(
                host.Services,
                builder.Configuration);
        }

        if (command == "bootstrap-development-admin")
        {
            return await BootstrapDevelopmentAdministratorAsync(
                host.Services,
                builder.Configuration,
                builder.Environment);
        }

        var runner = host.Services.GetRequiredService<IMigrationRunner>();
        var migrationsPath = ResolveMigrationsPath(
            builder.Configuration["Migrations:Path"]);

        switch (command)
        {
            case "status":
                {
                    var statuses = await runner.GetStatusAsync(migrationsPath);
                    WriteStatuses(statuses);
                    return HasInvalidStatus(statuses) ? 1 : 0;
                }
            case "validate":
                {
                    await runner.ValidateAsync(migrationsPath);
                    var statuses = await runner.GetStatusAsync(migrationsPath);
                    WriteStatuses(statuses);
                    Log.Information(
                        "Validated {MigrationCount} migration definitions.",
                        statuses.Count);
                    return 0;
                }
            case "migrate":
                {
                    var result = await runner.MigrateAsync(migrationsPath);
                    WriteStatuses(result.Statuses);
                    Log.Information(
                        "Applied {AppliedCount} migration(s).",
                        result.AppliedCount);
                    return 0;
                }
            default:
                throw new UnreachableException();
        }
    }
    catch (Exception exception)
    {
        Log.Error(exception, "Database migration command failed.");
        return 1;
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
}

static async Task<int> BootstrapDevelopmentAdministratorAsync(
    IServiceProvider services,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    if (!environment.IsDevelopment()
        && !string.Equals(environment.EnvironmentName, "Testing", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            "bootstrap-development-admin is disabled outside Development and Testing.");
        return 2;
    }

    var username = configuration["DevelopmentAdministrator:Username"];
    var displayName = configuration["DevelopmentAdministrator:DisplayName"];
    var email = configuration["DevelopmentAdministrator:Email"];
    var password = configuration["DevelopmentAdministrator:Password"];
    if (string.IsNullOrWhiteSpace(username)
        || string.IsNullOrWhiteSpace(displayName)
        || string.IsNullOrEmpty(password))
    {
        Console.Error.WriteLine(
            "DevelopmentAdministrator Username, DisplayName and Password must " +
            "be supplied through local user secrets or environment variables.");
        return 2;
    }

    await using var scope = services.CreateAsyncScope();
    var bootstrapper = scope.ServiceProvider
        .GetRequiredService<IAdministratorBootstrapService>();
    var administration = scope.ServiceProvider
        .GetRequiredService<ISecurityAdministrationService>();
    var users = await administration.ListUsersAsync(
        1, 100, null, CancellationToken.None);
    if (users.Total == 0)
    {
        await bootstrapper.BootstrapAsync(
            username, displayName, email, password, CancellationToken.None);
        Log.Information("Created the local development administrator account.");
        return 0;
    }

    var roles = await administration.ListRolesAsync(CancellationToken.None);
    var administratorRole = roles.SingleOrDefault(role =>
        string.Equals(role.Code, "administrator", StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            "The administrator role has not been seeded. Run migrations first.");
    var details = new List<UserDetails>();
    foreach (var user in users.Items)
    {
        details.Add(await administration.GetUserAsync(user.Id, CancellationToken.None));
    }

    var target = details.SingleOrDefault(user => string.Equals(
        user.Username.Trim(), username.Trim(), StringComparison.OrdinalIgnoreCase));
    var actor = details.FirstOrDefault(user => user.IsActive && user.Roles.Any(role =>
        string.Equals(role.Code, "administrator", StringComparison.Ordinal)));
    if (actor is null && target?.Roles.Any(role => string.Equals(
            role.Code, "administrator", StringComparison.Ordinal)) == true)
    {
        actor = target;
    }
    if (actor is null)
    {
        throw new InvalidOperationException(
            "An existing administrator is required to create or repair the " +
            "development account.");
    }

    if (target is null)
    {
        target = await administration.CreateUserAsync(
            new CreateUserCommand(
                username,
                displayName,
                email,
                password,
                [administratorRole.Id]),
            actor.Id,
            CancellationToken.None);
        Log.Information("Created the local development administrator account.");
        return 0;
    }

    if (!target.IsActive
        || !string.Equals(target.DisplayName, displayName.Trim(), StringComparison.Ordinal)
        || !string.Equals(target.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase)
        || !target.Roles.Any(role => role.Id == administratorRole.Id))
    {
        target = await administration.UpdateUserAsync(
            target.Id,
            new UpdateUserCommand(
                displayName,
                email,
                true,
                [administratorRole.Id],
                target.Version),
            actor.Id,
            CancellationToken.None);
    }

    await administration.ResetPasswordAsync(
        target.Id, password, actor.Id, CancellationToken.None);
    Log.Information("Refreshed the local development administrator account.");
    return 0;
}

static async Task<int> BootstrapAdministratorAsync(
    IServiceProvider services,
    IConfiguration configuration)
{
    var username = configuration["BootstrapAdministrator:Username"];
    var displayName = configuration["BootstrapAdministrator:DisplayName"];
    var email = configuration["BootstrapAdministrator:Email"];
    var password = configuration["BootstrapAdministrator:Password"];
    if (string.IsNullOrWhiteSpace(username)
        || string.IsNullOrWhiteSpace(displayName)
        || string.IsNullOrEmpty(password))
    {
        Console.Error.WriteLine(
            "BootstrapAdministrator Username, DisplayName and Password must " +
            "be supplied through local user secrets or environment variables.");
        return 2;
    }

    await using var scope = services.CreateAsyncScope();
    var bootstrapper = scope.ServiceProvider
        .GetRequiredService<IAdministratorBootstrapService>();
    var created = await bootstrapper.BootstrapAsync(
        username,
        displayName,
        email,
        password,
        CancellationToken.None);
    Log.Information(
        created
            ? "Created the first administrator account."
            : "Administrator bootstrap skipped because at least one user exists.");
    return 0;
}

static string ResolveMigrationsPath(string? configuredPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? "migrations"
        : configuredPath;

    return Path.GetFullPath(
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path));
}

static bool HasInvalidStatus(IReadOnlyList<MigrationStatus> statuses)
{
    return statuses.Any(status =>
        status.State is MigrationState.ChecksumMismatch or MigrationState.MissingFile);
}

static void WriteStatuses(IReadOnlyList<MigrationStatus> statuses)
{
    if (statuses.Count == 0)
    {
        Console.WriteLine("No migration files found.");
        return;
    }

    Console.WriteLine("Migration status:");
    foreach (var status in statuses)
    {
        var executedAt = status.ExecutedAtUtc?.ToString("O") ?? "-";
        Console.WriteLine($"{status.Id} | {status.State} | {executedAt}");
    }
}

static void WriteUsage()
{
    Console.WriteLine(
        "Usage: FlowHearth.DbMigrator " +
        "[status|validate|migrate|bootstrap-admin|bootstrap-development-admin]");
    Console.WriteLine(
        "Provide ConnectionStrings__FlowHearth through the environment or a local " +
        "development secret. Never pass secrets on the command line.");
    Console.WriteLine(
        "bootstrap-admin reads BootstrapAdministrator__Username, " +
        "DisplayName, optional Email and Password from configuration.");
    Console.WriteLine(
        "bootstrap-development-admin is restricted to Development/Testing and " +
        "reads DevelopmentAdministrator configuration from local secrets.");
}
