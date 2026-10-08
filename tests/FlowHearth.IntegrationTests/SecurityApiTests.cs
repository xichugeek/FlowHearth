using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class SecurityApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    private static readonly ulong[] OneRoleId = [1];

    [Fact]
    public async Task UnauthenticatedRequestReturns401WithoutRedirect()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/auth/me",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task MutationWithoutAntiforgeryTokenReturns400()
    {
        using var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = "admin", password = "Strong!Password1" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ViewerLoginCannotCallAdministratorApi()
    {
        using var client = CreateClient();
        var token = await GetCsrfTokenAsync(client);

        using var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { username = "viewer", password = "Strong!Password1" },
            token);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var usersResponse = await client.GetAsync(
            "/api/v1/users",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Forbidden, usersResponse.StatusCode);
        Assert.Null(usersResponse.Headers.Location);
    }

    [Fact]
    public async Task AdministratorLoginAndLogoutUseSecureCookieFlow()
    {
        using var client = CreateClient();
        var anonymousToken = await GetCsrfTokenAsync(client);

        using var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { username = "admin", password = "Strong!Password1" },
            anonymousToken);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Contains(
            loginResponse.Headers.GetValues("Set-Cookie"),
            value => value.Contains(".FlowHearth.Auth=", StringComparison.Ordinal)
                && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        using var usersResponse = await client.GetAsync(
            "/api/v1/users",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);

        var authenticatedToken = await GetCsrfTokenAsync(client);
        using var logoutResponse = await PostWithCsrfAsync(
            client,
            "/api/v1/auth/logout",
            new { },
            authenticatedToken);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var currentUserResponse = await client.GetAsync(
            "/api/v1/auth/me",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, currentUserResponse.StatusCode);
    }

    [Fact]
    public async Task PrivilegeEscalationFailureReturns403ProblemDetails()
    {
        using var client = CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { username = "admin", password = "Strong!Password1" },
            token);
        loginResponse.EnsureSuccessStatusCode();
        token = await GetCsrfTokenAsync(client);

        using var response = await PostWithCsrfAsync(
            client,
            "/api/v1/users",
            new
            {
                username = "blocked.user",
                displayName = "Blocked User",
                password = "Another!Password2",
                roleIds = OneRoleId,
            },
            token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "操作被拒绝",
            problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnhandledExceptionDoesNotLeakInternalDetails()
    {
        using var client = CreateClient();
        var token = await GetCsrfTokenAsync(client);
        using var loginResponse = await PostWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new { username = "admin", password = "Strong!Password1" },
            token);
        loginResponse.EnsureSuccessStatusCode();

        using var response = await client.GetAsync(
            "/api/v1/users/1",
            CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("NotSupportedException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Specified method", body, StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(
            "服务器内部错误",
            problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
    }

    private HttpClient CreateClient()
    {
        return factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/auth/csrf",
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(
            CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: CancellationToken.None);
        return document.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("CSRF token was not returned.");
    }

    private static Task<HttpResponseMessage> PostWithCsrfAsync(
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
        return client.SendAsync(request, CancellationToken.None);
    }
}
