using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Search;

namespace FlowHearth.IntegrationTests;

public sealed class DashboardSearchApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task DashboardUsesOnlyBusinessScopesGrantedToCaller()
    {
        using var client = CreateClient();
        await LoginAsync(client, "dashboard");

        var snapshot = await client.GetFromJsonAsync<DashboardSnapshot>(
            "/api/v1/dashboard");

        Assert.NotNull(snapshot);
        Assert.Equal(12, snapshot.Metrics.TotalCustomers);
        Assert.Null(snapshot.Metrics.ActiveOpportunities);
        Assert.Null(snapshot.Metrics.ActiveProjects);
        Assert.Null(snapshot.Metrics.OpenTickets);
        Assert.Single(snapshot.MonthlyNewCustomers);
        Assert.Empty(snapshot.OpportunityStages);
    }

    [Fact]
    public async Task SearchUsesOnlyBusinessScopesGrantedToCaller()
    {
        using var client = CreateClient();
        await LoginAsync(client, "search");

        var rows = await client.GetFromJsonAsync<GlobalSearchResult[]>(
            "/api/v1/search?q=%E6%B5%8B%E8%AF%95&limit=20");

        var result = Assert.Single(Assert.IsType<GlobalSearchResult[]>(rows));
        Assert.Equal("Contact", result.Kind);
        Assert.Equal("Customer", result.TargetType);
        Assert.Equal(42UL, result.TargetId);
    }

    [Fact]
    public async Task DedicatedPermissionsRemainRequired()
    {
        using var client = CreateClient();
        await LoginAsync(client, "files");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/dashboard")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/search?q=test")).StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = await CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { username, password = "Strong!Password1" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
