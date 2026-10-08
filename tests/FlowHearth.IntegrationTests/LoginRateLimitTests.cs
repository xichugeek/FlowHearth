using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FlowHearth.IntegrationTests;

public sealed class LoginRateLimitTests
{
    [Fact]
    public async Task EleventhLoginAttemptWithinMinuteIsRateLimited()
    {
        await using var factory = new SecurityApiApplicationFactory();
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
        using var csrfResponse = await client.GetAsync(
            "/api/v1/auth/csrf",
            CancellationToken.None);
        csrfResponse.EnsureSuccessStatusCode();
        await using var csrfStream = await csrfResponse.Content.ReadAsStreamAsync(
            CancellationToken.None);
        using var csrfDocument = await JsonDocument.ParseAsync(
            csrfStream,
            cancellationToken: CancellationToken.None);
        var token = csrfDocument.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("CSRF token was not returned.");

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 11; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/login")
            {
                Content = JsonContent.Create(
                    new { username = "unknown", password = "invalid" }),
            };
            request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(
                request,
                CancellationToken.None);
            statuses.Add(response.StatusCode);
        }

        Assert.All(
            statuses.Take(10),
            status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }
}
