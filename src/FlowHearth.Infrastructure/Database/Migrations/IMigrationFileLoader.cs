namespace FlowHearth.Infrastructure.Database.Migrations;

public interface IMigrationFileLoader
{
    IReadOnlyList<MigrationDefinition> Load(string migrationsPath);
}
