using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
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

namespace Ulbra.One.Registry.Application.Endpoints.Vendors.CreateVendor;

public sealed class CreateVendor : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateVendor")
            .WithTags("Registry")
            .WithSummary("Creates a vendor.")
            .WithDescription("Registers a vendor. The document (CNPJ or CPF) is normalized and validated with its "
                + "check digits, and the whole payload is validated before the handler runs, so a bad "
                + "field comes back as a 400 grouped by field name. Vendors are global to the "
                + "organization: one registration is payable by every company.")
            .RequirePermission(Permissions.RegistryWrite)
            .WithRequestNormalizer<VendorRequest>()
            .WithValidation<VendorRequest>()
            .Produces<VendorResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        VendorRequest req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        // Vendors are globally unique by document (across companies) — pre-check, then treat a racing
        // unique-index violation as the same conflict.
        var document = req.PersonType == PersonType.Company
            ? DocumentValidator.NormalizeCnpj(req.Document)
            : DocumentValidator.Normalize(req.Document);
        if (await uow.ReadSet<VendorEntity>().AnyAsync(v => v.Document == document, ct))
            return DuplicateDocument().ToCreated(v => $"/api/vendors/{v.Id}");

        var vendor = new VendorEntity(req.PersonType, req.Name, req.TradeName,
            req.Document, req.Email, req.Phone, req.Address.ToAddress(), req.PaymentTerms,
            req.BankInfo.ToBankInfo(), req.TaxRegime,
            new PaymentSchedule(req.PaymentScheduleMode, req.PaymentScheduleDay),
            req.CprbForeseen, req.SimplesAnexo, req.AcceptedPaymentMethods, req.WithholdingRates,
            req.AssociatedLedgerAccountIds?.Select(a => a.Value).ToList(),
            req.PrimaryCnaeCode, req.PrimaryCnaeDescription);
        await uow.AddAsync(vendor, ct);
        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            uow.ClearChangeTracker();
            return DuplicateDocument().ToCreated(v => $"/api/vendors/{v.Id}");
        }
        return Result.Success(VendorResponse.From(vendor)).ToCreated(v => $"/api/vendors/{v.Id}");
    }

    private static Result<VendorResponse> DuplicateDocument() =>
        Result.Failure<VendorResponse>(Error.Conflict(
            ErrorCodes.Vendor.DuplicateDocument, "Já existe um fornecedor com este CNPJ/CPF na organização."));
}
