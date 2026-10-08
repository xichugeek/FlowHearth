using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class ProjectApiTests(SecurityApiApplicationFactory factory) : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListButCannotCreateProject()
    {
        using var client = CreateClient(); var token = await LoginAsync(client, "viewer");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/projects")).StatusCode);
        using var create = await SendAsync(client, HttpMethod.Post, "/api/v1/projects", new { customerId = 42, name = "无权创建", contractAmount = 0, progressPercent = 0 }, token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateProjectMemberAndMilestone()
    {
        using var client = CreateClient(); var token = await LoginAsync(client, "admin");
        using var project = await SendAsync(client, HttpMethod.Post, "/api/v1/projects", new { customerId = 42, name = "API 项目", contractAmount = 1000, progressPercent = 0 }, token);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        using var member = await SendAsync(client, HttpMethod.Post, "/api/v1/projects/30/members", new { userId = 1, roleName = "项目经理" }, token);
        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        using var milestone = await SendAsync(client, HttpMethod.Post, "/api/v1/projects/30/milestones", new { name = "启动", isCompleted = false, sortOrder = 0 }, token);
        Assert.Equal(HttpStatusCode.Created, milestone.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> LoginAsync(HttpClient client, string username) { var token = await CsrfAsync(client); using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { username, password = "Strong!Password1" }, token); response.EnsureSuccessStatusCode(); return await CsrfAsync(client); }
    private static async Task<string> CsrfAsync(HttpClient client) { using var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode(); using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()); return document.RootElement.GetProperty("token").GetString()!; }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string uri, object payload, string token) { var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) }; request.Headers.Add("X-XSRF-TOKEN", token); return client.SendAsync(request); }
}
