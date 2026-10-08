using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlowHearth.Api.Health;

public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                }),
            correlationId = context.TraceIdentifier,
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
