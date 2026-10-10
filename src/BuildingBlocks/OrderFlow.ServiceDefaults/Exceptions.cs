using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace OrderFlow.ServiceDefaults;

public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public abstract string Title { get; }
}

public sealed class NotFoundException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string Title => "Resource not found";
}

public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string Title => "Conflict";
}

public sealed class BusinessRuleException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;
    public override string Title => "Business rule violated";
}

public sealed class UnauthorizedException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;
    public override string Title => "Unauthorized";
}

/// <summary>Maps exceptions to RFC 9457 problem details responses.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => JsonNaming.Camel(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                problem = new HttpValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Detail = validation.Errors.FirstOrDefault()?.ErrorMessage
                };
                break;
            case AppException app:
                problem = new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Message };
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message };
                break;
            default:
                logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "The server encountered an error. Please try again."
                };
                break;
        }

        httpContext.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}

internal static class JsonNaming
{
    public static string Camel(string name) =>
        string.Join('.', name.Split('.').Select(p => string.IsNullOrEmpty(p) ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
