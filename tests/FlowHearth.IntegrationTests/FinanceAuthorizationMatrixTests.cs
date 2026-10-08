using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class FinanceAuthorizationMatrixTests(
    SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    private static readonly string[] FinanceReadPaths =
    [
        "/api/v1/suppliers",
        "/api/v1/receivables",
        "/api/v1/receipts",
        "/api/v1/purchase-orders",
        "/api/v1/payables",
        "/api/v1/payments",
        "/api/v1/shipments",
        "/api/v1/shipments/equipment-candidates?projectId=30",
        "/api/v1/finance/summary",
        "/api/v1/finance/receivable-aging",
        "/api/v1/finance/payable-aging",
        "/api/v1/finance/dashboard",
        "/api/v1/finance/project-ranking",
        "/api/v1/customers/42/finance-summary",
        "/api/v1/projects/30/finance-summary",
        "/api/v1/customers/42/project-finance",
    ];

    private static readonly string[] KnownFinanceEntityPaths =
    [
        "/api/v1/suppliers/1",
        "/api/v1/receivables/1",
        "/api/v1/receipts/1",
        "/api/v1/purchase-orders/1",
        "/api/v1/payables/1",
        "/api/v1/payments/1",
        "/api/v1/shipments/1",
        "/api/v1/customers/42/finance-summary",
        "/api/v1/projects/30/finance-summary",
    ];

    public static IEnumerable<object[]> FinanceReadCases =>
        FinanceReadPaths.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(FinanceReadCases))]
    public async Task FinanceReadsRequireAuthentication(string path)
    {
        using var anonymous = CreateClient();

        using var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KnownIdsDoNotBypassModulePermission()
    {
        using var viewer = CreateClient();
        await LoginAsync(viewer, "viewer");

        foreach (var path in KnownFinanceEntityPaths)
        {
            using var response = await viewer.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task FinanceReaderCanUseEveryReadSurface()
    {
        using var reader = CreateClient();
        await LoginAsync(reader, "finance-reader");

        foreach (var path in FinanceReadPaths)
        {
            using var response = await reader.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task FinanceReaderCannotBypassWritePermissionsThroughDirectApiCalls()
    {
        using var reader = CreateClient();
        var csrf = await LoginAsync(reader, "finance-reader");
        var writes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Post, "/api/v1/suppliers"),
            (HttpMethod.Post, "/api/v1/receivables"),
            (HttpMethod.Post, "/api/v1/receipts"),
            (HttpMethod.Post, "/api/v1/receipts/1/allocations"),
            (HttpMethod.Post, "/api/v1/purchase-orders"),
            (HttpMethod.Post, "/api/v1/purchase-orders/1/receipts"),
            (HttpMethod.Post, "/api/v1/purchase-orders/1/payable-plan"),
            (HttpMethod.Post, "/api/v1/payables"),
            (HttpMethod.Post, "/api/v1/payments"),
            (HttpMethod.Post, "/api/v1/payments/1/allocations"),
            (HttpMethod.Delete, "/api/v1/payments/1/allocations/1"),
            (HttpMethod.Post, "/api/v1/shipments"),
            (HttpMethod.Post, "/api/v1/shipments/1/receive"),
            (HttpMethod.Post, "/api/v1/projects/30/receivable-plan"),
        };

        foreach (var (method, path) in writes)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = JsonContent.Create(new { version = 1 }),
            };
            request.Headers.Add("X-XSRF-TOKEN", csrf);
            using var response = await reader.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.Forbidden,
                $"Expected 403 for {method} {path}, received {(int)response.StatusCode}.");
        }
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new { username, password = "Strong!Password1" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await GetCsrfAsync(client);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
