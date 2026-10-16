using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Validation;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.AddRecurrenceCycle;

/// <summary>Launches the month's invoice of a recurring requisition already in payment (AEL-877): the
/// Requester records a RequisitionCycle and Finance materializes one more Payable for that competency — no
/// re-approval, the initial approval covers the future invoices (mirrors the legacy app). The cycle amount is
/// checked against the target competency's available budget (a hard block → 409 BudgetExceeded, as in approval;
/// resolved via an Adequação Orçamentária). Accepts <c>multipart/form-data</c>: the month's fatura is REQUIRED
/// (AEL-959) and rides the outbox message as a Fatura, and the Finance consumer copies it onto the cycle's
/// Payable at materialization — it never becomes a general requisition attachment.</summary>
public sealed class AddRecurrenceCycle : IEndpoint
{
    private const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cycles", Handle)
            .WithName("AddRecurrenceCycle").WithTags("Budgeting")
            .WithSummary("Launches a recurring requisition's monthly invoice.")
            .WithDescription("Records one more cycle on a recurring requisition already in payment, and "
                + "Finance materializes an extra Payable for that competency. There is no re-approval — the "
                + "initial approval covers the future invoices. The cycle's amount is still checked against "
                + "the target competency's available budget, and a breach is a hard 409 resolved by a budget "
                + "adjustment, exactly as at approval. Accepts `multipart/form-data`: the month's fatura is "
                + "required (400 if missing) and is copied onto the cycle's Payable as a Fatura, never becoming "
                + "a general requisition attachment.")
            .RequirePermission(Permissions.RequisitionsWrite).DisableAntiforgery()
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id,
        [FromForm] string competency,
        [FromForm] DateOnly dueDate,
        [FromForm] decimal amount,
        [FromForm] PaymentMethod paymentMethod,
        [FromForm] string? boletoDigitableLine,
        [FromForm] string? pixKeyOverride,
        IFormFile? file,
        UserContext user, IBudgetControl budgetControl, IFileStorageService storage,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (amount <= 0)
            return ErrorResults.InvalidRequest("The cycle amount must be greater than zero.", ErrorCodes.Requisition.InstallmentAmountInvalid);
        // Boleto: only relevant when the method is Boleto; when provided it must be 47 (bancário) or 48
        // (arrecadação/convênio, which always begins with 8) — the canonical rule (BoletoLine.IsValid),
        // so a 48-digit line without the 8 is rejected here instead of overflowing the writer at remittance time.
        // Blank is allowed on purpose (the barcode may be filled later); only a provided line is checked.
        if (paymentMethod == PaymentMethod.Boleto && !string.IsNullOrWhiteSpace(boletoDigitableLine)
            && !BoletoLine.IsValid(boletoDigitableLine))
            return ErrorResults.InvalidRequest(
                "The boleto linha digitável must have 47 (bancário) or 48 (convênio, starting with 8) digits.",
                ErrorCodes.Requisition.BoletoDigitableLineInvalid);
        // Conta Fixa (AEL-959): the fatura do mês is mandatory — every cycle must carry its invoice so the
        // materialized Payable clears the "documento fiscal" gate (Fatura OU NF) rather than stranding unpaid.
        if (file is null || file.Length == 0)
            return ErrorResults.InvalidRequest("Anexe a fatura do mês para lançar a fatura da Conta Fixa.", ErrorCodes.Requisition.CycleInvoiceRequired);

        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Cycles)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; keep the endpoint's 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        var boleto = paymentMethod == PaymentMethod.Boleto ? boletoDigitableLine : null;
        // PIX override (AEL-938): only meaningful when paid by PIX; overrides the vendor's key for this invoice only.
        var pix = paymentMethod == PaymentMethod.Pix ? pixKeyOverride : null;

        // AddCycle validates state/type/competency (throws → 409). It mutates the aggregate in memory only; nothing
        // persists until SaveChanges, so returning before it (on a budget breach) leaves no trace.
        RequisitionCycle cycle;
        try { cycle = requisition.AddCycle(competency, amount, dueDate, paymentMethod, boleto, Guid.Parse(user.UserId), clock.GetUtcNow(), pix); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        // Budget: the cycle competes for the target month's ceiling with the OTHER requisitions of the dimension
        // (exclude this one — the real cycle replaces its own estimate). Hard block, mirroring approval — EXCEPT
        // a requisition that BypassesBudgetCeiling (Conta Fixa), whose invoice amount varies month to month and
        // cannot be capped (AEL-881). Same semantics as approval, single source of truth in the domain.
        if (!requisition.BypassesBudgetCeiling)
        {
            var months = await budgetControl.DescribeAsync(requisition.CostCenterId, requisition.UnitId, cycle.Competency, cycle.Competency, requisition.Id, ct);
            var month = months.FirstOrDefault();
            if (month is null || !month.Budgeted || amount > month.Available + 0.005m)
                return ErrorResults.InvalidState(RequisitionErrors.BudgetExceeded.Description, RequisitionErrors.BudgetExceeded.Code);
        }

        // The fatura do mês (required above) rides the outbox message as a Fatura ref; the Finance consumer copies
        // it onto the cycle's Payable at materialization — so it lands on the right Payable (race-free) and never
        // on the general requisition list.
        if (file.Length > MaxFileSizeBytes)
            return ErrorResults.InvalidRequest($"File exceeds the {MaxFileSizeBytes / (1024 * 1024)}MB limit.", ErrorCodes.File.TooLarge);
        await using var stream = file.OpenReadStream();
        var validation = FileContentValidator.Validate(file.FileName, file.ContentType, stream);
        if (validation.IsFailure)
            return ErrorResults.InvalidRequest(validation.Error.Description, ErrorCodes.File.UnsupportedType);
        var key = await storage.SaveAsync(file.FileName, stream, ct);
        var attachment = new RequisitionAttachmentRef(file.FileName, file.ContentType, file.Length, key, Guid.Parse(user.UserId), "Invoice");

        await uow.AddAsync(cycle, ct);
        await uow.AddAsync(requisition.Transitions[^1], ct); // AddRecurrenceCycle trail (explicit Added — store-generated key)
        // Finance materializes the cycle's Payable (rides the EF bus outbox → atomic with the save). The optional
        // fiscal document ref travels with it so the consumer copies the file onto that Payable.
        await publish.Publish(RequisitionCycleAddedForPayment.From(requisition, cycle, attachment), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
