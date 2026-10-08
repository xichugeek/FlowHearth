using System.Diagnostics;
using Serilog.Context;

namespace FlowHearth.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaximumLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var requestedCorrelationId = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(requestedCorrelationId)
            ? requestedCorrelationId!
            : CreateCorrelationId();

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static string CreateCorrelationId()
    {
        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }
}
