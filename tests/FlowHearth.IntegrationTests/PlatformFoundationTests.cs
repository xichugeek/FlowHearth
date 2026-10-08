using System.Net;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class PlatformFoundationTests(ApiApplicationFactory factory)
    : IClassFixture<ApiApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    [Fact]
    public async Task LiveHealthReturnsHealthyAndCorrelationId()
    {
        using var response = await _client.GetAsync(
            "/health/live",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Single(values);

        await using var body = await response.Content.ReadAsStreamAsync(
            CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            body,
            cancellationToken: CancellationToken.None);

        Assert.Equal("healthy", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CorrelationIdPreservesValidCallerValue()
    {
        const string correlationId = "phase1-test-001";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-ID", correlationId);

        using var response = await _client.SendAsync(
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            correlationId,
            Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
    }

    [Fact]
    public async Task CorrelationIdReplacesInvalidCallerValue()
    {
        var invalidCorrelationId = new string('x', 129);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(
            "X-Correlation-ID",
            invalidCorrelationId);

        using var response = await _client.SendAsync(
            request,
            CancellationToken.None);

        var responseCorrelationId =
            Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.NotEqual(invalidCorrelationId, responseCorrelationId);
        Assert.InRange(responseCorrelationId.Length, 1, 128);
    }

    [Fact]
    public async Task ReadyHealthWithoutDatabaseConfigurationReturnsUnhealthy()
    {
        using var response = await _client.GetAsync(
            "/health/ready",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync(
            CancellationToken.None);
        Assert.Contains("unhealthy", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithCorrelationId()
    {
        using var response = await _client.GetAsync(
            "/route-that-does-not-exist",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(
            CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            body,
            cancellationToken: CancellationToken.None);

        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("correlationId").GetString()));
    }
}
