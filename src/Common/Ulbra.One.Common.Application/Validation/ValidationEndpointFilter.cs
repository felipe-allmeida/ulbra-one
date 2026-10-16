using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Common.Application.Validation;

/// <summary>
/// Validates the first argument of type <typeparamref name="TRequest"/> using a registered
/// <see cref="IValidator{T}"/>. Returns 400 + per-field errors when invalid; otherwise continues.
/// <para>
/// Goes through <see cref="ErrorResults"/> like every other error, so a validation failure carries the
/// same <c>trace_id</c> / <c>error_code</c> envelope as a handler's guard clause. The code is generic —
/// what identifies the failure here is the per-field <c>errors</c> dictionary, not the code.
/// </para>
/// </summary>
public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (validator is not null && request is not null)
        {
            var result = await validator.ValidateAsync(request);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return ErrorResults.ValidationError(errors, ErrorCodes.General.ValidationFailed);
            }
        }

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationEndpointFilter<TRequest>>();
}
