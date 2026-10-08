using FlowHearth.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Middleware;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, string, Exception>
        LogUnhandledException = LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1001, nameof(ApiExceptionHandler)),
            "Unhandled exception for request {Method} {Path}.");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            FlowHearthValidationException =>
                (StatusCodes.Status400BadRequest, "请求数据验证失败"),
            NotFoundException =>
                (StatusCodes.Status404NotFound, "请求的资源不存在"),
            ConflictException =>
                (StatusCodes.Status409Conflict, "数据冲突"),
            ForbiddenException =>
                (StatusCodes.Status403Forbidden, "操作被拒绝"),
            _ =>
                (StatusCodes.Status500InternalServerError, "服务器内部错误"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception);
        }

        httpContext.Response.StatusCode = statusCode;
        var problemDetails = exception is FlowHearthValidationException validationException
            ? new ValidationProblemDetails(
                validationException.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal))
            : new ProblemDetails();
        problemDetails.Status = statusCode;
        problemDetails.Title = title;
        problemDetails.Detail = statusCode == StatusCodes.Status500InternalServerError
            ? null
            : exception.Message;
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception,
            });
    }
}
