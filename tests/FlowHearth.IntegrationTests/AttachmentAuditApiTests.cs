using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class AttachmentAuditApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListAndDownloadButCannotUploadOrViewAudit()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "viewer");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/files?entityType=Customer&entityId=42")).StatusCode);
        using var download = await client.GetAsync("/api/v1/files/70/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("phase8.txt", download.Content.Headers.ContentDisposition?.FileNameStar);
        using var upload = await UploadAsync(client, token);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/audit-logs")).StatusCode);
    }

    [Fact]
    public async Task AttachmentPermissionDoesNotBypassEntityPermission()
    {
        using var client = CreateClient();
        _ = await LoginAsync(client, "files");

        using var response = await client.GetAsync("/api/v1/files?entityType=Customer&entityId=42");
        using var download = await client.GetAsync("/api/v1/files/70/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanUploadDeleteAndQueryAudit()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var upload = await UploadAsync(client, token);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/files/70?version=1");
        delete.Headers.Add("X-XSRF-TOKEN", token);
        using var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var audit = await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        using var detail = await client.GetAsync("/api/v1/audit-logs/80");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task InvalidAttachmentEntityTypeReturnsValidationProblem()
    {
        using var client = CreateClient();
        _ = await LoginAsync(client, "admin");

        using var response = await client.GetAsync(
            "/api/v1/files?entityType=999&entityId=42");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string token)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("phase8 attachment"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "phase8.txt");
        content.Add(new StringContent("API attachment"), "description");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files?entityType=Customer&entityId=42") { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await CsrfAsync(client);
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { username, password = "Strong!Password1" }, token);
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

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string uri, object payload, string token)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request);
    }
}
