using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class ShipmentApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/shipments")]
    [InlineData("/api/v1/projects/1/shipment-metrics")]
    [InlineData("/api/v1/equipment/1/shipment")]
    public async Task ShipmentReadsRequireAuthenticationAndPermission(string path)
    {
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);

        using var viewer = CreateClient();
        await LoginAsync(viewer, "viewer");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/shipments", "POST")]
    [InlineData("/api/v1/shipments/1/ship", "POST")]
    [InlineData("/api/v1/shipments/1/receive", "POST")]
    public async Task ShipmentMutationsRequireManagePermission(string path, string method)
    {
        using var viewer = CreateClient();
        var token = await LoginAsync(viewer, "viewer");
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { version = 1 }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await viewer.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { username, password = "Strong!Password1" }) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await GetCsrfAsync(client);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }
}
