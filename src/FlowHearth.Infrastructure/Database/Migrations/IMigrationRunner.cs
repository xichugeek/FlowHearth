namespace FlowHearth.Infrastructure.Database.Migrations;

public interface IMigrationRunner
{
    Task<IReadOnlyList<MigrationStatus>> GetStatusAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default);

    Task ValidateAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default);

    Task<MigrationExecutionResult> MigrateAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default);
}
