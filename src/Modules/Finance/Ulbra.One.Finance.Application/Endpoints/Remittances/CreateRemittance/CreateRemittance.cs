using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.CreateRemittance;

public sealed class CreateRemittance : IEndpoint
{
    /// <summary>
    /// <paramref name="InstallmentIds"/> present = generate from the operator's selection (cutoff
    /// ignored); absent = cutoff mode over every eligible installment.
    /// </summary>
    public sealed record Request(
        [property: SwaggerSchema("Bank account the payments will come out of. It decides which bank "
            + "layout the file is generated in.")]
        BankAccountId PayingBankAccountId,
        [property: SwaggerSchema("Include every eligible installment due up to this date. IGNORED when "
            + "InstallmentIds is sent.")]
        [property: SwaggerExample("2026-09-30")]
        DateOnly? DueCutoff,
        [property: SwaggerSchema("Exact installments to include. Sending this switches the endpoint to "
            + "selection mode and the cutoff is ignored. Capped at 500 per remittance.")]
        IReadOnlyList<InstallmentId>? InstallmentIds = null,
        [property: SwaggerSchema("Date the bank should execute the payments. Use it to schedule the batch "
            + "ahead; it cannot be in the past. Defaults to today.")]
        [property: SwaggerExample("2026-09-15")]
        DateOnly? PaymentDate = null);

    /// <summary>Keeps the generation retry loop bounded on absurd payloads.</summary>
    private const int MaxSelectionSize = 500;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle)
            .WithName("CreateRemittance").WithTags("Finance")
            .WithSummary("Generates a payment remittance file.")
            .WithDescription("Builds the CNAB file the bank executes, in one of two modes: send InstallmentIds to remit "
                + "exactly what the operator picked, or send only DueCutoff to sweep every eligible installment "
                + "due up to that date. Sending both is not a merge - the selection wins and the cutoff is "
                + "ignored. The included installments are locked into the remittance, so they stop appearing as "
                + "payable until the bank returns. Nothing is settled yet: payment only happens when the return "
                + "file confirms it.").RequirePermission(Permissions.TreasuryWrite)
            .Produces<CreateRemittanceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> Handle(
        Request req,
        UserContext user,
        IUnitOfWork uow,
        IRemittanceGenerationService service,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (req.InstallmentIds is { Count: > MaxSelectionSize })
            return ErrorResults.InvalidRequest($"Select at most {MaxSelectionSize} installments per remittance.", ErrorCodes.Remittance.SelectionTooLarge);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // A file dated before the day it reaches the bank is rejected there — refuse it here instead.
        if (req.PaymentDate is { } requested && requested < today)
            return ErrorResults.InvalidRequest("Payment date cannot be in the past.", ErrorCodes.Remittance.PaymentDateInPast);

        // Resolve companyId from the paying bank account (which owns the company association).
        // Company scope masked as the same 404 (AEL-764 pattern).
        var bankAccount = await uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == req.PayingBankAccountId.Value, ct);
        if (bankAccount is null || !user.CanAccess(bankAccount.CompanyId))
            return ErrorResults.NotFound("Bank account not found.", ErrorCodes.BankAccount.NotFound);

        var companyId = bankAccount.CompanyId;
        var generatedBy = user.UserId;
        var dueCutoff = req.DueCutoff ?? today;

        var result = await service.GenerateAsync(companyId, req.PayingBankAccountId.Value, dueCutoff, generatedBy,
            req.InstallmentIds?.Select(x => x.Value).ToList(), req.PaymentDate, ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                ErrorCodes.Remittance.NothingToRemit => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
                ErrorCodes.Remittance.BankNotSupported => ErrorResults.UnprocessableEntity(result.Error.Description, result.Error.Code),
                ErrorCodes.Remittance.MissingAgreementCode => ErrorResults.UnprocessableEntity(result.Error.Description, result.Error.Code),
                ErrorCodes.Remittance.InvalidPayerData => ErrorResults.UnprocessableEntity(result.Error.Description, result.Error.Code),
                ErrorCodes.Remittance.BankAccountNotFound => ErrorResults.NotFound(result.Error.Description, result.Error.Code),
                ErrorCodes.Remittance.CompanyNotFound => ErrorResults.NotFound(result.Error.Description, result.Error.Code),
                _ => ErrorResults.InvalidRequest(result.Error.Description, result.Error.Code),
            };
        }

        var outcome = result.Value;
        var body = new CreateRemittanceResponse(
            outcome.Remittances.Select(r => RemittanceResponse.From(r)).ToList(),
            outcome.Skipped.Select(SkippedInstallmentResponse.From).ToList());

        // One run now yields one file per payment category, so there is no single resource URL to point at.
        return TypedResults.Created("/api/remittances", body);
    }
}
