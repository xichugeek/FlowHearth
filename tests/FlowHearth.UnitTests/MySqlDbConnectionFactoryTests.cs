using FlowHearth.Infrastructure.Database;

namespace FlowHearth.UnitTests;

public sealed class MySqlDbConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnectionAsyncWithoutConfigurationThrowsClearError()
    {
        var factory = new MySqlDbConnectionFactory(null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.OpenConnectionAsync(
                CancellationToken.None));

        Assert.Contains("not configured", exception.Message);
        Assert.False(factory.IsConfigured);
    }
}
