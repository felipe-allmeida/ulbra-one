using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Registry.Application.Endpoints.Customers.ActivateCustomer;
using Ulbra.One.Registry.Application.Endpoints.Customers.CreateCustomer;
using Ulbra.One.Registry.Application.Endpoints.Customers.DeactivateCustomer;
using Ulbra.One.Registry.Application.Endpoints.Customers.DeleteCustomer;
using Ulbra.One.Registry.Application.Endpoints.Customers.GetCustomer;
using Ulbra.One.Registry.Application.Endpoints.Customers.ListCustomers;
using Ulbra.One.Registry.Application.Endpoints.Customers.UpdateCustomer;
using Ulbra.One.Registry.Application.Endpoints.Employees.ActivateEmployee;
using Ulbra.One.Registry.Application.Endpoints.Employees.CreateEmployee;
using Ulbra.One.Registry.Application.Endpoints.Employees.DeactivateEmployee;
using Ulbra.One.Registry.Application.Endpoints.Employees.DeleteEmployee;
using Ulbra.One.Registry.Application.Endpoints.Employees.GetEmployee;
using Ulbra.One.Registry.Application.Endpoints.Employees.ListEmployees;
using Ulbra.One.Registry.Application.Endpoints.Employees.UpdateEmployee;
using Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCep;
using Ulbra.One.Registry.Application.Endpoints.Lookup.LookupCnpj;
using Ulbra.One.Registry.Application.Endpoints.Vendors.ActivateVendor;
using Ulbra.One.Registry.Application.Endpoints.Vendors.CreateVendor;
using Ulbra.One.Registry.Application.Endpoints.Vendors.DeactivateVendor;
using Ulbra.One.Registry.Application.Endpoints.Vendors.DeleteVendor;
using Ulbra.One.Registry.Application.Endpoints.Vendors.GetVendor;
using Ulbra.One.Registry.Application.Endpoints.Vendors.ListVendors;
using Ulbra.One.Registry.Application.Endpoints.Vendors.UpdateVendor;
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
        var vendors = builder.MapGroup("/vendors");
        vendors.MapEndpoint<ListVendors>();
        vendors.MapEndpoint<GetVendor>();
        vendors.MapEndpoint<CreateVendor>();
        vendors.MapEndpoint<UpdateVendor>();
        vendors.MapEndpoint<DeactivateVendor>();
        vendors.MapEndpoint<ActivateVendor>();
        vendors.MapEndpoint<DeleteVendor>();

        var customers = builder.MapGroup("/customers");
        customers.MapEndpoint<ListCustomers>();
        customers.MapEndpoint<GetCustomer>();
        customers.MapEndpoint<CreateCustomer>();
        customers.MapEndpoint<UpdateCustomer>();
        customers.MapEndpoint<DeactivateCustomer>();
        customers.MapEndpoint<ActivateCustomer>();
        customers.MapEndpoint<DeleteCustomer>();

        var employees = builder.MapGroup("/employees");
        employees.MapEndpoint<ListEmployees>();
        employees.MapEndpoint<GetEmployee>();
        employees.MapEndpoint<CreateEmployee>();
        employees.MapEndpoint<UpdateEmployee>();
        employees.MapEndpoint<DeactivateEmployee>();
        employees.MapEndpoint<ActivateEmployee>();
        employees.MapEndpoint<DeleteEmployee>();

        var lookup = builder.MapGroup("/lookup");
        lookup.MapEndpoint<LookupCep>();
        lookup.MapEndpoint<LookupCnpj>();

        return builder;
    }
}
