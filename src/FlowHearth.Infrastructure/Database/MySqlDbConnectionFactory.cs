using System.Data.Common;
using Dapper;
using FlowHearth.Application.Abstractions.Data;
using MySqlConnector;

namespace FlowHearth.Infrastructure.Database;

public sealed class MySqlDbConnectionFactory(string? connectionString)
    : IDbConnectionFactory
{
    static MySqlDbConnectionFactory()
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }

    private readonly string? _connectionString = connectionString;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async ValueTask<DbConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "The FlowHearth database connection is not configured.");
        }

        var connection = new MySqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
