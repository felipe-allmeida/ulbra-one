using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.UnitTests.Validation;

public class ValidationEndpointFilterTests
{
    private record Req(string Name);
    private class ReqValidator : AbstractValidator<Req>
    {
        public ReqValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private static EndpointFilterInvocationContext ContextWith(Req req)
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<Req>, ReqValidator>();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return EndpointFilterInvocationContext.Create(http, req);
    }

    [Fact]
    public async Task Invalid_ShortCircuits_With_400()
    {
        var filter = new ValidationEndpointFilter<Req>();
        var result = await filter.InvokeAsync(ContextWith(new Req("")), _ => ValueTask.FromResult<object?>("next"));
        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Contains(nameof(Req.Name), problem.ProblemDetails.Errors);
        // The filter goes through ErrorResults, so it carries the same envelope as a handler's guard.
        Assert.Equal(ErrorCodes.General.ValidationFailed, problem.ProblemDetails.Extensions[ErrorResults.ErrorCodeKey]);
        Assert.True(problem.ProblemDetails.Extensions.ContainsKey(ErrorResults.TraceIdKey));
    }

    [Fact]
    public async Task Valid_CallsNext()
    {
        var filter = new ValidationEndpointFilter<Req>();
        var result = await filter.InvokeAsync(ContextWith(new Req("ok")), _ => ValueTask.FromResult<object?>("next"));
        Assert.Equal("next", result);
    }
}
