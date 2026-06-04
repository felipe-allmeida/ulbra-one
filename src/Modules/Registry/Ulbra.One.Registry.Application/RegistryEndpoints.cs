using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Registry.Application.Endpoints;
using Ulbra.One.Registry.Application.Validation;

namespace Ulbra.One.Registry.Application;

public static class RegistryEndpoints
{
    /// <summary>Registers the FluentValidation validators used by the Registry endpoint filters.</summary>
    public static IServiceCollection AddRegistryValidators(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<VendorRequestValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapRegistryEndpoints(this IEndpointRouteBuilder builder)
    {
        new VendorsEndpoint().Map(builder);
        new CustomersEndpoint().Map(builder);
        new EmployeesEndpoint().Map(builder);
        new LookupEndpoint().Map(builder);
        return builder;
    }
}
