using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class PayablePaymentApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/payables")]
    [InlineData("/api/v1/payments")]
    public async Task FinancePaymentEndpointsRequireAuthenticationAndPermission(
        string path)
    {
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var viewer = CreateClient();
        await LoginAsync(viewer, "viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task PaymentMutationsAndPurchasePayablePlanRequireManagePermission()
    {
        using var viewer = CreateClient();
        var token = await LoginAsync(viewer, "viewer");

        using var payable = await SendAsync(viewer, HttpMethod.Post, "/api/v1/payables",
            new { supplierId = 1, projectId = 1, payableType = "PurchasePayment", title = "测试应付", amount = 1, dueDate = "2026-09-02" }, token);
        using var payment = await SendAsync(viewer, HttpMethod.Post, "/api/v1/payments",
            new { supplierId = 1, paymentDate = "2026-09-02", amount = 1, paymentMethod = "BankTransfer" }, token);
        using var allocation = await SendAsync(viewer, HttpMethod.Post, "/api/v1/payments/1/allocations",
            new { paymentVersion = 1, allocations = new[] { new { payableId = 1, amount = 1 } } }, token);
        using var plan = await SendAsync(viewer, HttpMethod.Post, "/api/v1/purchase-orders/1/payable-plan",
            new { items = new[] { new { payableType = "PurchasePayment", title = "测试应付", amount = 1, dueDate = "2026-09-02" } } }, token);

        Assert.Equal(HttpStatusCode.Forbidden, payable.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, payment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, allocation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, plan.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await GetCsrfAsync(client);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login",
            new { username, password = "Strong!Password1" }, token);
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

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string uri, object payload, string token)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }
}
