using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowHearth.Domain.Opportunities;

namespace FlowHearth.IntegrationTests;

public sealed class OpportunityApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListButCannotCreateOpportunities()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "viewer");

        using var list = await client.GetAsync("/api/v1/opportunities", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var create = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/opportunities",
            new
            {
                customerId = 42,
                title = "无权创建",
                expectedAmount = 100m,
                probabilityPercent = 10,
            },
            token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateTransitionAndConvertOpportunity()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var create = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/opportunities",
            new
            {
                customerId = 42,
                title = "集成测试商机",
                expectedAmount = 100000m,
                probabilityPercent = 30,
            },
            token);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var transition = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/opportunities/20/transition",
            new { stage = OpportunityStage.Won, version = 1 },
            token);
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);

        using var conversion = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/opportunities/20/convert-to-project",
            new { version = 2 },
            token);
        Assert.Equal(HttpStatusCode.OK, conversion.StatusCode);
    }

    [Fact]
    public async Task StaleOpportunityUpdateReturns409ProblemDetails()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            "/api/v1/opportunities/20",
            new
            {
                customerId = 42,
                title = "冲突商机",
                expectedAmount = 100m,
                probabilityPercent = 20,
                version = 99,
            },
            token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await GetCsrfTokenAsync(client);
        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username, password = "Strong!Password1" },
            token);
        response.EnsureSuccessStatusCode();
        return await GetCsrfTokenAsync(client);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object payload,
        string token)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request, CancellationToken.None);
    }
}
