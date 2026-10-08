using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class PurchaseOrderApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task PurchaseEndpointsRequireAuthenticationAndPurchasePermission()
    {
        using var anonymous = CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/purchase-orders")).StatusCode);

        using var viewer = CreateClient();
        var token = await LoginAsync(viewer, "viewer");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.GetAsync("/api/v1/purchase-orders")).StatusCode);
        using var create = await SendAsync(
            viewer,
            "/api/v1/purchase-orders",
            new
            {
                supplierId = 1,
                projectId = 1,
                orderDate = "2026-09-01",
                items = new[]
                {
                    new { itemName = "测试", quantity = 1, unit = "台", unitPrice = 1 },
                },
            },
            token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
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
        using var response = await SendAsync(
            client,
            "/api/v1/auth/login",
            new { username, password = "Strong!Password1" },
            token);
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
        HttpClient client,
        string uri,
        object payload,
        string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }
}
