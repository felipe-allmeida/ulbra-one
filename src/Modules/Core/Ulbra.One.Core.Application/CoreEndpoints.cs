using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Common.Infrastructure.Extensions;
using Ulbra.One.Core.Application.Endpoints.Companies.CreateCompany;
using Ulbra.One.Core.Application.Endpoints.Companies.ListCompanies;
using Ulbra.One.Core.Application.Endpoints.Companies.UpdateCompany;
using Ulbra.One.Core.Application.Endpoints.CostCenters.CreateCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.GetCostCenter;
using Ulbra.One.Core.Application.Endpoints.CostCenters.ListCostCenters;
using Ulbra.One.Core.Application.Endpoints.CostCenters.UpdateCostCenter;
using Ulbra.One.Core.Application.Endpoints.Ping;
using Ulbra.One.Core.Application.Endpoints.Units.CreateUnit;
using Ulbra.One.Core.Application.Endpoints.Units.ListUnits;
using Ulbra.One.Core.Application.Endpoints.Units.UpdateUnit;

namespace Ulbra.One.Core.Application;

public static class CoreEndpoints
{
    public static IEndpointRouteBuilder MapCoreEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapEndpoint<Ping>();

        var companies = builder.MapGroup("/companies");
        companies.MapEndpoint<ListCompanies>();
        companies.MapEndpoint<CreateCompany>();
        companies.MapEndpoint<UpdateCompany>();

        var units = builder.MapGroup("/units");
        units.MapEndpoint<ListUnits>();
        units.MapEndpoint<CreateUnit>();
        units.MapEndpoint<UpdateUnit>();

        var costCenters = builder.MapGroup("/cost-centers");
        costCenters.MapEndpoint<ListCostCenters>();
        costCenters.MapEndpoint<GetCostCenter>();
        costCenters.MapEndpoint<CreateCostCenter>();
        costCenters.MapEndpoint<UpdateCostCenter>();

        return builder;
    }
}
