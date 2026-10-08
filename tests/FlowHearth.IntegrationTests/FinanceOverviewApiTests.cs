using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowHearth.Application.Finance;

namespace FlowHearth.IntegrationTests;

public sealed class FinanceOverviewApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/finance/summary")]
    [InlineData("/api/v1/finance/receivable-aging")]
    [InlineData("/api/v1/finance/payable-aging")]
    [InlineData("/api/v1/finance/dashboard")]
    [InlineData("/api/v1/finance/project-ranking?sortBy=grossProfit")]
    [InlineData("/api/v1/customers/42/finance-summary")]
    [InlineData("/api/v1/projects/30/finance-summary")]
    public async Task OperatingFinanceEndpointsRequireAuthentication(string path)
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task BusinessReadPermissionsDoNotBypassFinancePermissions()
    {
        using var dashboard = CreateClient();
        await LoginAsync(dashboard, "dashboard");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await dashboard.GetAsync("/api/v1/finance/summary")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await dashboard.GetAsync("/api/v1/finance/dashboard")).StatusCode);

        using var viewer = CreateClient();
        await LoginAsync(viewer, "viewer");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/v1/customers/42/finance-summary")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/v1/projects/30/finance-summary")).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanReadCompanyCustomerAndProjectSummaries()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin");

        var company = await client.GetFromJsonAsync<CompanyFinanceSummary>(
            "/api/v1/finance/summary");
        var dashboard = await client.GetFromJsonAsync<FinanceDashboardSnapshot>(
            "/api/v1/finance/dashboard");
        var customer = await client.GetFromJsonAsync<CustomerFinanceOverview>(
            "/api/v1/customers/42/finance-summary");
        var project = await client.GetFromJsonAsync<ProjectFinanceOverview>(
            "/api/v1/projects/30/finance-summary");

        Assert.Equal(new DateOnly(2026, 9, 2), company?.AsOfDate);
        Assert.Equal(new DateOnly(2026, 9, 2), dashboard?.AsOfDate);
        Assert.Equal(50m, Assert.Single(dashboard!.CashFlowTrend).NetAmount);
        Assert.Equal(42UL, customer?.CustomerId);
        Assert.Equal(30UL, project?.ProjectId);
    }

    [Fact]
    public async Task ProjectRankingRejectsUnknownMetricBeforeQueryingRepository()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin");

        using var response = await client.GetAsync(
            "/api/v1/finance/project-ranking?sortBy=updatedAt&limit=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
