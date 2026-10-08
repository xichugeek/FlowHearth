using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowHearth.Application.Settings;

namespace FlowHearth.IntegrationTests;

public sealed class SettingsApiTests(SecurityApiApplicationFactory factory)
    : IClassFixture<SecurityApiApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task RuntimeSettingsAreAvailableToEveryAuthenticatedUser()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer");

        var runtime = await client.GetFromJsonAsync<RuntimeSettings>(
            "/api/v1/settings/runtime");

        Assert.NotNull(runtime);
        Assert.Equal(10, runtime.DefaultPageSize);
        Assert.Equal("TN", runtime.NumberPrefixes.Project);
        Assert.Single(runtime.Lookups[LookupDictionaryCodes.CustomerIndustry]);
    }

    [Fact]
    public async Task SettingsReaderCanViewButCannotMutate()
    {
        using var client = CreateClient();
        await LoginAsync(client, "settings");

        var snapshot = await client.GetFromJsonAsync<SettingsAdministrationSnapshot>(
            "/api/v1/settings",
            JsonOptions);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Dictionaries);

        var token = await CsrfAsync(client);
        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/settings/system/{SystemSettingKeys.DefaultPageSize}",
            new { value = "50", version = 1 },
            token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateLookupAndUpdateSetting()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin");
        var token = await CsrfAsync(client);

        using var createResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/settings/lookup-items",
            new
            {
                dictionaryCode = LookupDictionaryCodes.CustomerIndustry,
                value = "半导体",
                label = "半导体",
                description = "测试",
                sortOrder = 15,
            },
            token);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var updateResponse = await SendWithCsrfAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/settings/system/{SystemSettingKeys.DefaultPageSize}",
            new { value = "50", version = 1 },
            token);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SystemSettingDetails>(
            JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("50", updated.Value);
        Assert.Equal(2UL, updated.Version);
    }

    [Fact]
    public async Task UnauthenticatedAndOrdinaryViewerCannotOpenAdministration()
    {
        using var anonymousClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/v1/settings/runtime")).StatusCode);

        using var viewerClient = CreateClient();
        await LoginAsync(viewerClient, "viewer");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewerClient.GetAsync("/api/v1/settings")).StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var token = await CsrfAsync(client);
        using var response = await SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            new { username, password = "Strong!Password1" },
            token);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
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
        return client.SendAsync(request);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
