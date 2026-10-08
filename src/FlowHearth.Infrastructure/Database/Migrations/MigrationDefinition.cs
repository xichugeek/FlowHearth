namespace FlowHearth.Infrastructure.Database.Migrations;

public sealed record MigrationDefinition(
    string Id,
    string FilePath,
    string Checksum,
    string Sql);
