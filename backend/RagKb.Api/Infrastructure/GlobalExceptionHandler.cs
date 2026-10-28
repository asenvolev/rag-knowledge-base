using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RagKb.Api.Infrastructure;

/// <summary>Turns exceptions into RFC 7807 ProblemDetails without leaking internals.</summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            BadRequestException e => (StatusCodes.Status400BadRequest, "Invalid request", e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            AiProviderException e => (StatusCodes.Status502BadGateway, "AI provider error", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Invalid request", e.Message),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested
                => (499, "Client closed request", "The request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred.")
        };

        if (status >= 500 && status != 502)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning("{Status} for {Method} {Path}: {Message}", status, context.Request.Method, context.Request.Path, exception.Message);

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
