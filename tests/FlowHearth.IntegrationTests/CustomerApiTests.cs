using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class CustomerApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    [Fact]
    public async Task ReadOnlyUserCanListButCannotCreateCustomers()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "viewer");

        using var list = await client.GetAsync("/api/v1/customers", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        await using (var stream = await list.Content.ReadAsStreamAsync(CancellationToken.None))
        using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None))
        {
            Assert.Equal(10, document.RootElement.GetProperty("pageSize").GetInt32());
            var customer = document.RootElement.GetProperty("items")[0];
            Assert.Equal("张工", customer.GetProperty("primaryContactName").GetString());
            Assert.Equal("13800000000", customer.GetProperty("primaryContactMethod").GetString());
            Assert.Equal("Active", customer.GetProperty("status").GetString());
            Assert.Equal("Unrated", customer.GetProperty("level").GetString());
        }

        using var create = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/customers",
            new { name = "无权创建" },
            token);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateCustomerAndContact()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var createCustomer = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/customers",
            new { name = "集成测试客户" },
            token);
        Assert.Equal(HttpStatusCode.Created, createCustomer.StatusCode);

        token = await GetCsrfTokenAsync(client);
        using var createContact = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/customers/42/contacts",
            new { name = "张工", isPrimary = true },
            token);
        Assert.Equal(HttpStatusCode.Created, createContact.StatusCode);
    }

    [Fact]
    public async Task StaleCustomerUpdateReturns409ProblemDetails()
    {
        using var client = CreateClient();
        var token = await LoginAsync(client, "admin");

        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            "/api/v1/customers/42",
            new { name = "冲突客户", version = 99 },
            token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> LoginAsync(HttpClient client, string username)
    {
        var token = await GetCsrfTokenAsync(client);
        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username, password = "Strong!Password1" },
            token);
        response.EnsureSuccessStatusCode();
        return await GetCsrfTokenAsync(client);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object payload,
        string token)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return client.SendAsync(request, CancellationToken.None);
    }
}
