using MassTransit;
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
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using CPCreatePayable = Ulbra.One.Finance.Application.Endpoints.Payables.CreatePayable.CreatePayable;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.UpdatePayable;

public sealed class UpdatePayable : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Vendor to be paid. Must reference an existing vendor (vendors are global to the organization).")]
        public VendorId VendorId { get; set; }

        [SwaggerSchema("Cost center the expense is charged to.")]
        public CostCenterId CostCenterId { get; set; }

        // Optional unit (estabelecimento) tag for accounting tracking; ignored for requisition-sourced payables.
        [SwaggerSchema("Unit (estabelecimento) tag for accounting tracking. Ignored on a payable that came "
            + "from a requisition, which owns its own dimensions.")]
        public UnitId? UnitId { get; set; }

        [SwaggerSchema("Expense category.")]
        public ExpenseCategoryId? CategoryId { get; set; }

        [SwaggerSchema("What is being paid for.")]
        [SwaggerExample("Manutencao preventiva do ar-condicionado - agosto/2026")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Number of the vendor's document (nota fiscal, fatura or contract).")]
        [SwaggerExample("NF 12345")]
        [NormalizeText] public string? DocumentNumber { get; set; }

        [SwaggerSchema("Issue date of the document — the accounting competency anchor.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly? IssueDate { get; set; }

        [SwaggerSchema("The complete installment schedule. It REPLACES the current one, so any installment "
            + "left out is dropped. Send either this or Generate.")]
        public List<CPCreatePayable.InstallmentInput>? Installments { get; set; }

        [SwaggerSchema("Rebuild the schedule from a total, a count and a first due date. The alternative "
            + "to Installments.")]
        public CPCreatePayable.GenerateInput? Generate { get; set; }

        [SwaggerSchema("Expense ledger account for the accounting posting.")]
        public LedgerAccountId? AccountingAccountId { get; set; }

        [SwaggerSchema("Bank account the payment comes out of. Omit to use the company's default.")]
        public BankAccountId? PaymentBankAccountId { get; set; }

        [SwaggerSchema("How this payable is paid. Omit to inherit the paying account's default method.")]
        public PaymentMethod? PaymentMethod { get; set; }

        // Service/Product/null (undetermined). Applied unconditionally: this PUT is full-replace,
        // and the form sends null when the user clears the selector back to "undetermined".
        [SwaggerSchema("Service, product, or null for undetermined. Applied unconditionally — this is a "
            + "full replace, so sending null genuinely clears the type rather than leaving it as it was.")]
        public PayableType? Type { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}", Handle).WithName("UpdatePayable").WithTags("Finance")
            .WithSummary("Updates a payable.")
            .WithDescription("Replaces the payable's editable fields and its whole installment schedule. "
                + "A full replace, not a patch. A payable that already has a payment registered cannot be "
                + "edited at all (409): payments are append-only, so correcting one means reversing the "
                + "payment first. The owning company is immutable.")
            .RequirePermission(Permissions.PayablesWrite).WithRequestNormalizer<Request>()
            .Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, Request req,
        IUnitOfWork uow, IPublishEndpoint publish, ChargeReferenceDuplicateChecker chargeReferences,
        CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return ErrorResults.NotFound("Payable not found.", ErrorCodes.Payable.NotFound);
        if (p.Installments.Any(i => i.Payments.Any(pay => !pay.IsReversed)))
            return ErrorResults.InvalidRequest("Cannot edit a payable that already has registered payments.", ErrorCodes.Payable.HasPayments);

        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == p.CompanyId, ct);
        if (cc is null) return ErrorResults.InvalidRequest("Cost center is invalid for this company.", ErrorCodes.Payable.CostCenterInvalid);
        var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == req.VendorId.Value, ct);
        if (vendor is null) return ErrorResults.InvalidRequest("Vendor not found.", ErrorCodes.Vendor.NotFound);

        if (req.UnitId is { } unitId)
        {
            var unitOk = await uow.ReadSet<Unit>().AnyAsync(u => u.Id == unitId.Value && u.CompanyId == p.CompanyId, ct);
            if (!unitOk) return ErrorResults.InvalidRequest("Unit is invalid for this company.", ErrorCodes.Payable.UnitInvalid);
        }

        if (req.CategoryId is { } catId)
        {
            var catOk = await uow.ReadSet<ExpenseCategory>().AnyAsync(c => c.Id == catId.Value && c.Active, ct);
            if (!catOk) return ErrorResults.InvalidRequest("Category is invalid.", ErrorCodes.Payable.CategoryInvalid);
        }

        List<Installment> installments;
        if (req.Generate is { } g)
        {
            if (g.TotalAmount <= 0 || g.Count < 1)
                return ErrorResults.InvalidRequest("Invalid installment generation.", ErrorCodes.Payable.InstallmentsInvalid);
            installments = Payable.GenerateInstallments(g.TotalAmount, g.Count, g.FirstDueDate, g.IntervalMonths);
        }
        else if (req.Installments is { Count: > 0 } rows)
        {
            if (rows.Any(r => r.Amount <= 0))
                return ErrorResults.InvalidRequest("Each installment amount must be greater than zero.", ErrorCodes.Payable.InstallmentsInvalid);
            installments = rows.Select((r, i) => new Installment(i + 1, r.DueDate, r.Amount, r.BoletoDigitableLine, pixKeyOverride: r.PixKeyOverride, pixPayloadEmv: r.PixPayloadEmv)).ToList();
        }
        else return ErrorResults.InvalidRequest("Provide installments or a generation spec.", ErrorCodes.Payable.InstallmentsInvalid);

        // Shift-left the boleto check (AEL-932): a provided linha digitável must be well-formed here instead
        // of silently dropping out at remittance time. A blank line stays allowed (fill-the-barcode-later).
        if (BoletoEntryValidation.Problem(installments.Select(i => i.BoletoDigitableLine).ToList()) is { } boletoProblem)
            return boletoProblem;
        if (PixPayloadEmvEntryValidation.Problem(installments.Select(i => (i.PixKeyOverride, i.PixPayloadEmv)).ToList()) is { } pixCpProblem)
            return pixCpProblem;

        // Exclude this payable AND the requisition that generated it: a requisition-sourced title is a copy of
        // the plan's lines, so without the origin exclusion an untouched boleto would conflict with itself.
        Guid[] ownLines = p.OriginRequisitionId is { } originId ? [p.Id, originId] : [p.Id];
        if (await chargeReferences.ProblemAsync(ChargeReferenceKind.BoletoLine, installments.Select(i => i.BoletoDigitableLine).ToList(),
                ownLines, ErrorCodes.Payable.BoletoDigitableLineDuplicate, ct) is { } duplicateProblem)
            return duplicateProblem;

        // Um QR dinâmico é uma cobrança emitida, como o boleto (AEL-963). O estático fica de fora: é a chave
        // do fornecedor embrulhada em QR, e repeti-la entre pagamentos é o caso normal.
        if (await chargeReferences.ProblemAsync(ChargeReferenceKind.PixPayloadEmv,
                installments.Select(i => i.PixPayloadEmv).ToList(), ownLines,
                ErrorCodes.Payable.PixPayloadEmvDuplicate, ct) is { } pixDuplicate)
            return pixDuplicate;

        var oldInstallments = p.Installments.ToList();
        try { p.Update(vendor.Id, vendor.Name, req.DocumentNumber, req.IssueDate, req.Description, cc.Id, req.CategoryId?.Value, installments, req.UnitId?.Value); }
        catch (InvalidOperationException ex) { return ErrorResults.InvalidState(ex.Message, ErrorCodes.Payable.NotEditable); }

        if (req.AccountingAccountId is not null)
        {
            // A scheduled payable's incurral is already posted — its ledger account must move through the
            // classify/reclassify action (which rebooks the Razão), not a plain edit. Same account is a no-op.
            if (p.Status is PayableStatus.Scheduled or PayableStatus.Paid)
            {
                if (p.AccountingAccountId != req.AccountingAccountId.Value.Value)
                    return ErrorResults.InvalidState("Use the classify action to change the ledger account of a scheduled payable.", ErrorCodes.Payable.NotEditable);
            }
            else
            {
                var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, ct);
                if (err is not null) return ErrorResults.InvalidRequest("Invalid chart account.", err);
                p.Classify(req.AccountingAccountId?.Value);
            }
        }
        p.SetPaymentSource(req.PaymentBankAccountId?.Value, req.PaymentMethod);
        p.SetType(req.Type);
        // EF does not auto-track changes to the private List<T> backing field, so we manage the
        // installment set explicitly: delete old rows and register new ones.
        uow.RemoveRange(oldInstallments);
        foreach (var inst in p.Installments)
            await uow.AddAsync(inst, ct);
        // The bus outbox captures the event in the same transaction as the edit — the withholding
        // recalculation consumer only ever sees committed state.
        await publish.Publish(new PayableUpdated(p.Id, p.CompanyId), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
