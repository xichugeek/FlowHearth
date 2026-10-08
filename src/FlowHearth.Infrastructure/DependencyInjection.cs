using FlowHearth.Application.Abstractions.Data;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Audit;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Search;
using FlowHearth.Application.Security;
using FlowHearth.Application.Service;
using FlowHearth.Application.Settings;
using FlowHearth.Infrastructure.Attachments;
using FlowHearth.Infrastructure.Audit;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Dashboard;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Database.Migrations;
using FlowHearth.Infrastructure.Equipment;
using FlowHearth.Infrastructure.Finance;
using FlowHearth.Infrastructure.Health;
using FlowHearth.Infrastructure.Opportunities;
using FlowHearth.Infrastructure.Projects;
using FlowHearth.Infrastructure.Search;
using FlowHearth.Infrastructure.Security;
using FlowHearth.Infrastructure.Service;
using FlowHearth.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlowHearth.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FlowHearth");
        var commandTimeoutSeconds = ParseCommandTimeout(
            configuration["Database:CommandTimeoutSeconds"]);

        services.AddSingleton<IDbConnectionFactory>(
            new MySqlDbConnectionFactory(connectionString));
        services.AddSingleton<IMigrationFileLoader, MigrationFileLoader>();
        services.AddSingleton<IMigrationRunner>(serviceProvider =>
            new MySqlMigrationRunner(
                serviceProvider.GetRequiredService<IDbConnectionFactory>(),
                serviceProvider.GetRequiredService<IMigrationFileLoader>(),
                commandTimeoutSeconds));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHashService, AspNetPasswordHashService>();
        services.AddScoped<MySqlSecurityRepository>();
        services.AddScoped<IAuthenticationRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<MySqlSecurityRepository>());
        services.AddScoped<IAdministratorBootstrapRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<MySqlSecurityRepository>());
        services.AddScoped<ISecurityAdministrationRepository>(serviceProvider =>
            serviceProvider.GetRequiredService<MySqlSecurityRepository>());
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAdministratorBootstrapService, AdministratorBootstrapService>();
        services.AddScoped<ISecurityAdministrationService, SecurityAdministrationService>();
        services.AddScoped<ICustomerRepository, MySqlCustomerRepository>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOpportunityRepository, MySqlOpportunityRepository>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IProjectRepository, MySqlProjectRepository>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IEquipmentRepository, MySqlEquipmentRepository>();
        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<ISupplierRepository, MySqlSupplierRepository>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseOrderRepository, MySqlPurchaseOrderRepository>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IReceivableRepository, MySqlReceivableRepository>();
        services.AddScoped<IReceivableService, ReceivableService>();
        services.AddScoped<IPayableRepository, MySqlPayableRepository>();
        services.AddScoped<IPayableService, PayableService>();
        services.AddScoped<IShipmentRepository, MySqlShipmentRepository>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IFinanceOverviewRepository, MySqlFinanceOverviewRepository>();
        services.AddScoped<IFinanceOverviewService, FinanceOverviewService>();
        services.AddScoped<IServiceTicketRepository, MySqlServiceTicketRepository>();
        services.AddScoped<IServiceTicketService, ServiceTicketService>();
        var fileStorageRoot = configuration["FileStorage:RootPath"];
        var maximumFileSizeBytes = ParseMaximumFileSize(
            configuration["FileStorage:MaximumFileSizeBytes"]);
        services.AddSingleton<IFileStorage>(serviceProvider =>
            new LocalFileStorage(
                fileStorageRoot ?? string.Empty,
                maximumFileSizeBytes,
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IAttachmentRepository, MySqlAttachmentRepository>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IAuditLogRepository, MySqlAuditLogRepository>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IDashboardRepository, MySqlDashboardRepository>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IGlobalSearchRepository, MySqlGlobalSearchRepository>();
        services.AddScoped<IGlobalSearchService, GlobalSearchService>();
        services.AddScoped<ISettingsRepository, MySqlSettingsRepository>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsService>(serviceProvider =>
            serviceProvider.GetRequiredService<SettingsService>());
        services.AddScoped<IRuntimeSettingsProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<SettingsService>());

        services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy(),
                tags: ["live"])
            .AddCheck<MySqlHealthCheck>("mysql", tags: ["ready"]);

        return services;
    }

    private static int ParseCommandTimeout(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return 30;
        }

        if (!int.TryParse(configuredValue, out var value) || value is < 1 or > 300)
        {
            throw new InvalidOperationException(
                "Database:CommandTimeoutSeconds must be between 1 and 300.");
        }

        return value;
    }

    private static long ParseMaximumFileSize(string? configuredValue)
    {
        const long defaultMaximum = 20 * 1024 * 1024;
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return defaultMaximum;
        }

        if (!long.TryParse(configuredValue, out var value)
            || value is < 1 or > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "FileStorage:MaximumFileSizeBytes must be between 1 byte and 100 MiB.");
        }

        return value;
    }
}
