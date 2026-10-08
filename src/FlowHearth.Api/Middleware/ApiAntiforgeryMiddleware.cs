using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Middleware;

public sealed class ApiAntiforgeryMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethods.Get,
            HttpMethods.Head,
            HttpMethods.Options,
            HttpMethods.Trace,
        };

    public async Task InvokeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IProblemDetailsService problemDetailsService)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1")
            && !SafeMethods.Contains(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await problemDetailsService.TryWriteAsync(
                    new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "CSRF 验证失败",
                            Detail = "缺少或无效的请求防伪令牌。",
                            Extensions =
                            {
                                ["correlationId"] = context.TraceIdentifier,
                            },
                        },
                    });
                return;
            }
        }

        await next(context);
    }
}
