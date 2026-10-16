using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Extensions;
using Ulbra.One.Core.Application.Endpoints.Companies.ActivateCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.CreateCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.DeactivateCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.DeleteCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.ListCompanies;
using Ulbra.One.Core.Application.Endpoints.Companies.UpdateCompany;
using Ulbra.One.Core.Application.Endpoints.CostCenters.CreateCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.GetCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.ListCostCenters;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UpdateCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.CreateUnitBudget;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.DeleteUnitBudget;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.ListUnitBudgets;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UnitBudgets.UpdateUnitBudget;
using Ulbra.One.Core.Application.Endpoints.Units.CreateUnit;
using Ulbra.One.Core.Application.Endpoints.Units.ListUnits;
using Ulbra.One.Core.Application.Endpoints.Units.UpdateUnit;

using Ulbra.One.Core.Application.Validation;

namespace Ulbra.One.Core.Application;

public static class CoreEndpoints
{
    /// <summary>Registers the module's request validators, resolved by <c>.WithValidation&lt;T&gt;()</c>.</summary>
    public static IServiceCollection AddCoreValidators(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateCompanyRequestValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapCoreEndpoints(this IEndpointRouteBuilder builder)
    {
        var companies = builder.MapGroup("/companies");
        companies.MapEndpoint<ListCompanies>();
        companies.MapEndpoint<CreateCompany>();
        companies.MapEndpoint<UpdateCompany>();
        companies.MapEndpoint<DeactivateCompany>();
        companies.MapEndpoint<ActivateCompany>();
        companies.MapEndpoint<DeleteCompany>();

        var units = builder.MapGroup("/units");
        units.MapEndpoint<ListUnits>();
        units.MapEndpoint<CreateUnit>();
        units.MapEndpoint<UpdateUnit>();

        var costCenters = builder.MapGroup("/cost-centers");
        costCenters.MapEndpoint<ListCostCenters>();
        costCenters.MapEndpoint<GetCostCenter>();
        costCenters.MapEndpoint<CreateCostCenter>();
        costCenters.MapEndpoint<UpdateCostCenter>();
        // Nested budget cells owned by the cost center (create-by-period, list, update, delete).
        costCenters.MapEndpoint<ListUnitBudgets>();
        costCenters.MapEndpoint<CreateUnitBudget>();
        costCenters.MapEndpoint<UpdateUnitBudget>();
        costCenters.MapEndpoint<DeleteUnitBudget>();

        return builder;
    }
}
