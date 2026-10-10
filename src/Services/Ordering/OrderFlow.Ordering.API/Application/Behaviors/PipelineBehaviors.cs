using System.Diagnostics;
using FluentValidation;
using MediatR;

namespace OrderFlow.Ordering.API.Application.Behaviors;

/// <summary>Runs all FluentValidation validators for a request before its handler executes.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count > 0) throw new ValidationException(failures);
        }
        return await next(ct);
    }
}

/// <summary>Structured timing log for every command and query.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await next(ct);
        }
        finally
        {
            logger.LogInformation("Handled {Request} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
        }
    }
}
