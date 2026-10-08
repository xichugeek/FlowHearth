namespace FlowHearth.Infrastructure.Database.Migrations;

public enum MigrationState
{
    Pending,
    Applied,
    ChecksumMismatch,
    MissingFile,
}

public sealed record MigrationStatus(
    string Id,
    string? FileChecksum,
    string? AppliedChecksum,
    DateTime? ExecutedAtUtc,
    MigrationState State);

public sealed record MigrationExecutionResult(
    int AppliedCount,
    IReadOnlyList<MigrationStatus> Statuses);
