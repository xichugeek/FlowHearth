using System.Data.Common;

namespace FlowHearth.Application.Abstractions.Data;

public interface IDbConnectionFactory
{
    bool IsConfigured { get; }

    ValueTask<DbConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);
}
