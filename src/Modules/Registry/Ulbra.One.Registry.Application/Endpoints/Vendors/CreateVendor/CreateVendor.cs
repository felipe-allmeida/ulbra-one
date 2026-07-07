using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.CreateVendor;

public sealed class CreateVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateVendor")
            .WithTags("Registry")
            .RequireAuthorization()
            .WithRequestNormalizer<VendorRequest>()
            .WithValidation<VendorRequest>()
            .Produces<VendorResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        VendorRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = new VendorEntity(req.CompanyId.Value, req.PersonType, req.Name, req.TradeName,
            req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.PaymentTerms,
            req.BankInfo.ToBankInfo(), req.TaxRegime,
            new PaymentSchedule(req.PaymentScheduleMode, req.PaymentScheduleDay),
            req.CprbForeseen, req.SimplesAnexo);
        await uow.AddAsync(vendor, ct);
        await uow.SaveChangesAsync(ct);
        return Result.Success(VendorResponse.From(vendor)).ToCreated(v => $"/api/vendors/{v.Id}");
    }
}
