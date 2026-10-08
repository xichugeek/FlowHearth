using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Search;
using FlowHearth.Infrastructure.Customers;
using FlowHearth.Infrastructure.Database;
using FlowHearth.Infrastructure.Search;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowHearth.IntegrationTests;

[Collection(RealMySqlTestGroup.Name)]
public sealed class RealMySqlUnicodeSearchTests
{
    [Theory]
    [InlineData("经瑞")]
    [InlineData("星河🚀")]
    [InlineData("星河%_=")]
    [Trait("Category", "LocalDatabase")]
    public async Task UnicodeSearchPreservesRelevancePagingEscapingAndScope(string term)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWHEARTH_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var factory = new MySqlDbConnectionFactory(connectionString);
        var customers = new CustomerService(new MySqlCustomerRepository(factory), TimeProvider.System);
        var search = new GlobalSearchService(new MySqlGlobalSearchRepository(factory));
        var scope = new GlobalSearchAccessScope(true, false, false, false);
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var marker = Guid.NewGuid().ToString("N")[..12];
        var query = $"{term}{marker}";
        var ids = new List<ulong>();
        try
        {
            foreach (var name in new[] { query, $"{query}工厂", $"零部件{query}工厂" })
            {
                ids.Add(await connection.QuerySingleAsync<ulong>(
                    """
                    INSERT INTO customers (customer_code,name,version,created_at_utc,updated_at_utc)
                    VALUES (@Code,@Name,1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
                    SELECT LAST_INSERT_ID();
                    """, new { Code = $"CU-US-{marker}-{ids.Count}", Name = name }));
            }

            var results = await search.SearchAsync($"  {query}  ", 20, scope, CancellationToken.None);
            Assert.Equal(ids, results.Select(item => item.TargetId));
            Assert.All(results, item => Assert.Equal("Customer", item.Kind));
            var limited = await search.SearchAsync(query, 1, scope, CancellationToken.None);
            Assert.Equal(ids[0], Assert.Single(limited).TargetId);
            Assert.Empty(await search.SearchAsync(query, 20,
                new GlobalSearchAccessScope(false, true, false, false), CancellationToken.None));

            var pageOne = await customers.ListAsync(1, 2, query, "active", null, null, "code", false, null, CancellationToken.None);
            var pageTwo = await customers.ListAsync(2, 2, query, "active", null, null, "code", false, null, CancellationToken.None);
            Assert.Equal(3, pageOne.Total);
            Assert.Equal(3, pageTwo.Total);
            Assert.Equal(ids, pageOne.Items.Concat(pageTwo.Items).Select(item => item.Id));

            // Exercise URL decoding/controllers against real MySQL; only auth is fake.
            using var api = new SecurityApiApplicationFactory();
            using var host = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICustomerService>();
                services.RemoveAll<IGlobalSearchService>();
                services.AddSingleton<ICustomerService>(customers);
                services.AddSingleton<IGlobalSearchService>(search);
            }));
            using var client = host.CreateClient();
            var encodedQuery = Uri.EscapeDataString(query);
            var globalUrl = $"/api/v1/search?q={encodedQuery}&limit=20";
            using var anonymous = await client.GetAsync(globalUrl);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var csrf = await client.GetFromJsonAsync<JsonDocument>("/api/v1/auth/csrf");
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { username = "search", password = "Strong!Password1" }),
            };
            login.Headers.Add("X-XSRF-TOKEN", csrf!.RootElement.GetProperty("token").GetString());
            using var authenticated = await client.SendAsync(login);
            authenticated.EnsureSuccessStatusCode();
            var apiResults = await client.GetFromJsonAsync<GlobalSearchResult[]>(globalUrl);
            Assert.NotNull(apiResults);
            Assert.Equal(ids, apiResults.Select(item => item.TargetId));
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            jsonOptions.Converters.Add(new JsonStringEnumConverter());
            var apiPage = await client.GetFromJsonAsync<PagedResult<CustomerSummary>>(
                $"/api/v1/customers?search={encodedQuery}&page=1&pageSize=20&sortBy=code&sortDescending=false",
                jsonOptions);
            Assert.NotNull(apiPage);
            Assert.Equal(3, apiPage.Total);
            Assert.Equal(ids, apiPage.Items.Select(item => item.Id));

            var codeMatches = await search.SearchAsync($"cu-us-{marker}-0", 20, scope, CancellationToken.None);
            Assert.Equal(ids[0], Assert.Single(codeMatches).TargetId);
            var missingQuery = $"未命中{query}";
            Assert.Empty(await search.SearchAsync(missingQuery, 20, scope, CancellationToken.None));
            var missing = await customers.ListAsync(1, 20, missingQuery, "active", null, null, "name", false, null, CancellationToken.None);
            Assert.Empty(missing.Items);
            Assert.Equal(0, missing.Total);

            // Replacing literal LIKE metacharacters must not turn them into wildcards.
            if (term.Contains('%'))
            {
                await connection.ExecuteAsync("UPDATE customers SET name=@Name WHERE id=@Id;",
                    new { Name = query.Replace("%_=", "XYZ", StringComparison.Ordinal), Id = ids[2] });
                var literalMatches = await search.SearchAsync(query, 20, scope, CancellationToken.None);
                Assert.Equal(ids.Take(2), literalMatches.Select(item => item.TargetId));
                var literalPage = await customers.ListAsync(1, 20, query, "active", null, null, "code", false, null, CancellationToken.None);
                Assert.Equal(2, literalPage.Total);
                Assert.Equal(ids.Take(2), literalPage.Items.Select(item => item.Id));
            }

            await connection.ExecuteAsync("UPDATE customers SET archived_at_utc=UTC_TIMESTAMP(6) WHERE id=@Id;", new { Id = ids[0] });
            var archived = await customers.ListAsync(1, 20, query, "archived", null, null, "name", false, null, CancellationToken.None);
            Assert.Equal(ids[0], Assert.Single(archived.Items).Id);
            var withArchived = await search.SearchAsync(query, 20, scope, CancellationToken.None);
            Assert.True(withArchived.Single(item => item.TargetId == ids[0]).IsArchived);
        }
        finally
        {
            if (ids.Count > 0)
                await connection.ExecuteAsync("DELETE FROM customers WHERE id IN @Ids;", new { Ids = ids });
        }
    }
}
