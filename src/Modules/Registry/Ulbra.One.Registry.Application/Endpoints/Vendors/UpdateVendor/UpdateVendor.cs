using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.Results;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Registry.Application.Contracts;
using Ulbra.One.Registry.Application.Validation;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.UpdateVendor;

public sealed class UpdateVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateVendor")
            .WithTags("Registry")
            .RequireAuthorization()
            .WithRequestNormalizer<VendorRequest>()
            .WithValidation<VendorRequest>()
            .Produces<VendorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        VendorRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = await uow.Set<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        if (vendor is null)
            return Result.Failure<VendorResponse>(Error.NotFound("Vendor.NotFound", "Vendor not found.")).ToOk();
        vendor.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
            req.Address.ToAddress(), req.PaymentTerms, req.BankInfo.ToBankInfo(), req.TaxRegime,
            new PaymentSchedule(req.PaymentScheduleMode, req.PaymentScheduleDay),
            req.CprbForeseen, req.SimplesAnexo, req.AcceptedPaymentMethods);
        await uow.SaveChangesAsync(ct);
        return Result.Success(VendorResponse.From(vendor)).ToOk();
    }
}
