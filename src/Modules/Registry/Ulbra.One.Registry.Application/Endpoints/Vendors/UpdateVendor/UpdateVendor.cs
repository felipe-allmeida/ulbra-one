using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;
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
            .WithSummary("Updates a vendor.")
            .WithDescription("Replaces the vendor's editable fields — this is a full replace, not a patch, so "
                + "omitting a field clears it. Same normalization and validation as creation.")
            .RequirePermission(Permissions.RegistryWrite)
            .WithRequestNormalizer<VendorRequest>()
            .WithValidation<VendorRequest>()
            .Produces<VendorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        VendorId id,
        VendorRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var vendor = await uow.Set<VendorEntity>().FirstOrDefaultAsync(v => v.Id == id.Value, ct);
        if (vendor is null)
            return Result.Failure<VendorResponse>(Error.NotFound(ErrorCodes.Vendor.NotFound, "Vendor not found.")).ToOk();

        // Vendors are globally unique by document (across companies) — reject a document already used by
        // another vendor; a racing unique-index violation maps to the same conflict.
        var document = req.PersonType == PersonType.Company
            ? DocumentValidator.NormalizeCnpj(req.Document)
            : DocumentValidator.Normalize(req.Document);
        if (await uow.ReadSet<VendorEntity>().AnyAsync(v => v.Id != id.Value && v.Document == document, ct))
            return DuplicateDocument();

        vendor.Update(req.PersonType, req.Name, req.TradeName, req.Document, req.Email, req.Phone,
            req.Address.ToAddress(), req.PaymentTerms, req.BankInfo.ToBankInfo(), req.TaxRegime,
            new PaymentSchedule(req.PaymentScheduleMode, req.PaymentScheduleDay),
            req.CprbForeseen, req.SimplesAnexo, req.AcceptedPaymentMethods, req.WithholdingRates,
            req.AssociatedLedgerAccountIds?.Select(a => a.Value).ToList(),
            req.PrimaryCnaeCode, req.PrimaryCnaeDescription);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            uow.ClearChangeTracker();
            return DuplicateDocument();
        }
        return Result.Success(VendorResponse.From(vendor)).ToOk();
    }

    private static IResult DuplicateDocument() =>
        Result.Failure<VendorResponse>(Error.Conflict(
            ErrorCodes.Vendor.DuplicateDocument, "Já existe um fornecedor com este CNPJ/CPF na organização.")).ToOk();
}
