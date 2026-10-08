using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class EquipmentApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListButCannotCreateEquipment()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "viewer");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/equipment")).StatusCode);
        using var create = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/equipment",
            new { customerId = 42, projectId = 30, name = "无权创建", category = "PLC" },
            token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateEquipmentTechnicalHistory()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var equipment = await SendAsync(client, HttpMethod.Post, "/api/v1/equipment", new { customerId = 42, projectId = 30, name = "API 设备", category = "PLC" }, token);
        Assert.Equal(HttpStatusCode.Created, equipment.StatusCode);
        using var component = await SendAsync(client, HttpMethod.Post, "/api/v1/equipment/50/components", new { category = "PLC", name = "主控制器", quantity = 1, sortOrder = 0 }, token);
        Assert.Equal(HttpStatusCode.Created, component.StatusCode);
        using var parameter = await SendAsync(client, HttpMethod.Post, "/api/v1/equipment/50/parameters", new { parameterGroup = "Drive", name = "工作模式", value = "Auto / Manual / Jog", sortOrder = 0 }, token);
        Assert.Equal(HttpStatusCode.Created, parameter.StatusCode);
        using var version = await SendAsync(client, HttpMethod.Post, "/api/v1/equipment/50/versions", new { versionType = "PLC Program", versionLabel = "v1.0.0", gitCommit = "abc123", changelog = "Initial release" }, token);
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await CsrfAsync(client);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { username, password = "Strong!Password1" }, token);
        response.EnsureSuccessStatusCode();
        return await CsrfAsync(client);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object payload,
        string token)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }
}
