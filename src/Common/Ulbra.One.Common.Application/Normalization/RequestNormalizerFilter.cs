using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Ulbra.One.Common.Application.Normalization;

/// <summary>
/// Normalizes the first argument of type <typeparamref name="TRequest"/> before the handler runs:
/// trims and collapses whitespace on each <see cref="NormalizeTextAttribute"/>-annotated string
/// property, and strips formatting from each <see cref="NormalizeCnpjAttribute"/>-annotated one.
/// </summary>
public sealed class RequestNormalizerFilter<TRequest> : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var arg in context.Arguments)
        {
            if (arg is TRequest request)
            {
                TextNormalizer.Normalize(request);
                CnpjNormalizer.Normalize(request);
                break;
            }
        }

        return next(context);
    }
}

public static class RequestNormalizerExtensions
{
    public static RouteHandlerBuilder WithRequestNormalizer<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<RequestNormalizerFilter<TRequest>>();
}
