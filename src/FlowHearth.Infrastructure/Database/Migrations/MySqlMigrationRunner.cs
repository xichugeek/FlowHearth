using System.Data;
using System.Data.Common;
using Dapper;
using FlowHearth.Application.Abstractions.Data;

namespace FlowHearth.Infrastructure.Database.Migrations;

public sealed class MySqlMigrationRunner(
    IDbConnectionFactory connectionFactory,
    IMigrationFileLoader fileLoader,
    int commandTimeoutSeconds) : IMigrationRunner
{
    private const string MigrationLockName = "flowhearth_schema_migrations";

    public async Task<IReadOnlyList<MigrationStatus>> GetStatusAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default)
    {
        var files = fileLoader.Load(migrationsPath);
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);
        var applied = await ReadAppliedMigrationsAsync(
            connection,
            cancellationToken);

        return BuildStatuses(files, applied);
    }

    public async Task ValidateAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default)
    {
        var statuses = await GetStatusAsync(migrationsPath, cancellationToken);
        ThrowIfInvalid(statuses);
    }

    public async Task<MigrationExecutionResult> MigrateAsync(
        string migrationsPath,
        CancellationToken cancellationToken = default)
    {
        var files = fileLoader.Load(migrationsPath);
        await using var connection =
            await connectionFactory.OpenConnectionAsync(cancellationToken);

        await AcquireLockAsync(connection, cancellationToken);

        try
        {
            await EnsureMigrationTableAsync(connection, cancellationToken);
            var applied = await ReadAppliedMigrationsAsync(
                connection,
                cancellationToken,
                migrationTableKnownToExist: true);
            var initialStatuses = BuildStatuses(files, applied);
            ThrowIfInvalid(initialStatuses);

            var appliedCount = 0;
            foreach (var migration in files.Where(file =>
                         !applied.ContainsKey(file.Id)))
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        migration.Sql,
                        commandTimeout: commandTimeoutSeconds,
                        commandType: CommandType.Text,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO schema_migrations
                            (migration_id, checksum, executed_at_utc)
                        VALUES
                            (@MigrationId, @Checksum, UTC_TIMESTAMP(6));
                        """,
                        new
                        {
                            MigrationId = migration.Id,
                            migration.Checksum,
                        },
                        commandTimeout: commandTimeoutSeconds,
                        commandType: CommandType.Text,
                        cancellationToken: cancellationToken));

                appliedCount++;
            }

            var finalApplied = await ReadAppliedMigrationsAsync(
                connection,
                cancellationToken,
                migrationTableKnownToExist: true);
            var finalStatuses = BuildStatuses(files, finalApplied);
            ThrowIfInvalid(finalStatuses);

            return new MigrationExecutionResult(appliedCount, finalStatuses);
        }
        finally
        {
            try
            {
                await ReleaseLockAsync(connection, CancellationToken.None);
            }
            catch (DbException)
            {
                // Closing the connection releases MySQL named locks. Do not mask
                // the migration result when an already-broken connection cannot
                // execute the explicit release command.
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, AppliedMigration>>
        ReadAppliedMigrationsAsync(
            DbConnection connection,
            CancellationToken cancellationToken,
            bool migrationTableKnownToExist = false)
    {
        if (!migrationTableKnownToExist &&
            !await MigrationTableExistsAsync(connection, cancellationToken))
        {
            return new Dictionary<string, AppliedMigration>(StringComparer.Ordinal);
        }

        var records = await connection.QueryAsync<AppliedMigration>(
            new CommandDefinition(
                """
                SELECT
                    migration_id AS MigrationId,
                    checksum AS Checksum,
                    executed_at_utc AS ExecutedAtUtc
                FROM schema_migrations
                ORDER BY migration_id;
                """,
                commandTimeout: commandTimeoutSeconds,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));

        return records.ToDictionary(
            record => record.MigrationId,
            StringComparer.Ordinal);
    }

    private async Task<bool> MigrationTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                  AND table_name = 'schema_migrations';
                """,
                commandTimeout: commandTimeoutSeconds,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));

        return count == 1;
    }

    private async Task EnsureMigrationTableAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations
                (
                    migration_id VARCHAR(150) NOT NULL,
                    checksum CHAR(64) NOT NULL,
                    executed_at_utc DATETIME(6) NOT NULL,
                    PRIMARY KEY (migration_id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
                """,
                commandTimeout: commandTimeoutSeconds,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));
    }

    private static async Task AcquireLockAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var acquired = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                "SELECT GET_LOCK(@LockName, @TimeoutSeconds);",
                new
                {
                    LockName = MigrationLockName,
                    TimeoutSeconds = 30,
                },
                commandTimeout: 35,
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));

        if (acquired != 1)
        {
            throw new MigrationException(
                "Could not acquire the FlowHearth database migration lock.");
        }
    }

    private static async Task ReleaseLockAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                "SELECT RELEASE_LOCK(@LockName);",
                new { LockName = MigrationLockName },
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));
    }

    private static MigrationStatus[] BuildStatuses(
        IReadOnlyList<MigrationDefinition> files,
        IReadOnlyDictionary<string, AppliedMigration> applied)
    {
        var statuses = new List<MigrationStatus>(files.Count + applied.Count);

        foreach (var file in files)
        {
            if (!applied.TryGetValue(file.Id, out var record))
            {
                statuses.Add(new MigrationStatus(
                    file.Id,
                    file.Checksum,
                    null,
                    null,
                    MigrationState.Pending));
                continue;
            }

            statuses.Add(new MigrationStatus(
                file.Id,
                file.Checksum,
                record.Checksum,
                DateTime.SpecifyKind(record.ExecutedAtUtc, DateTimeKind.Utc),
                string.Equals(
                    file.Checksum,
                    record.Checksum,
                    StringComparison.OrdinalIgnoreCase)
                    ? MigrationState.Applied
                    : MigrationState.ChecksumMismatch));
        }

        var fileIds = files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        statuses.AddRange(applied.Values
            .Where(record => !fileIds.Contains(record.MigrationId))
            .Select(record => new MigrationStatus(
                record.MigrationId,
                null,
                record.Checksum,
                DateTime.SpecifyKind(record.ExecutedAtUtc, DateTimeKind.Utc),
                MigrationState.MissingFile)));

        return statuses
            .OrderBy(status => status.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ThrowIfInvalid(IReadOnlyList<MigrationStatus> statuses)
    {
        var invalid = statuses.FirstOrDefault(status =>
            status.State is MigrationState.ChecksumMismatch or MigrationState.MissingFile);

        if (invalid is not null)
        {
            throw new MigrationException(
                $"Migration '{invalid.Id}' has invalid state '{invalid.State}'. " +
                "Applied migrations are immutable and must remain in the release package.");
        }
    }

    private sealed class AppliedMigration
    {
        public required string MigrationId { get; init; }

        public required string Checksum { get; init; }

        public DateTime ExecutedAtUtc { get; init; }
    }
}
