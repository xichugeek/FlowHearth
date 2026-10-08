namespace FlowHearth.Infrastructure.Database.Migrations;

public sealed class MigrationException : Exception
{
    public MigrationException(string message)
        : base(message)
    {
    }
}
