using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class ServiceTicketApiTests(SecurityApiApplicationFactory factory) : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListButCannotCreateTicket()
    {
        using var client = CreateClient(); var token = await LoginAsync(client, "viewer");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/service-tickets")).StatusCode);
        using var create = await SendAsync(client, HttpMethod.Post, "/api/v1/service-tickets", new { customerId = 42, title = "无权创建", priority = "P2" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateAssignTransitionAndRecord()
    {
        using var client = CreateClient(); var token = await LoginAsync(client, "admin");
        using var ticket = await SendAsync(client, HttpMethod.Post, "/api/v1/service-tickets", new { customerId = 42, projectId = 30, equipmentId = 50, title = "API 工单", priority = "P1" }, token);
        Assert.Equal(HttpStatusCode.Created, ticket.StatusCode);
        using var assign = await SendAsync(client, HttpMethod.Post, "/api/v1/service-tickets/60/assign", new { userId = 1, version = 1 }, token);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        using var transition = await SendAsync(client, HttpMethod.Post, "/api/v1/service-tickets/60/transition", new { status = "InProgress", version = 1 }, token);
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);
        using var record = await SendAsync(client, HttpMethod.Post, "/api/v1/service-tickets/60/records", new { recordType = "Diagnosis", content = "检查安全回路", durationMinutes = 10 }, token);
        Assert.Equal(HttpStatusCode.Created, record.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> LoginAsync(HttpClient client, string username) { var token = await CsrfAsync(client); using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { username, password = "Strong!Password1" }, token); response.EnsureSuccessStatusCode(); return await CsrfAsync(client); }
    private static async Task<string> CsrfAsync(HttpClient client) { using var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode(); using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()); return document.RootElement.GetProperty("token").GetString()!; }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string uri, object payload, string token) { var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) }; request.Headers.Add("X-XSRF-TOKEN", token); return client.SendAsync(request); }
}
