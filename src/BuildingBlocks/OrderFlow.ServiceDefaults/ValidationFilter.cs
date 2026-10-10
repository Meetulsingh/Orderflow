using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace OrderFlow.ServiceDefaults;

/// <summary>Runs the registered FluentValidation validator for a minimal API request body.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (validator is not null && argument is not null)
        {
            var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }
        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
