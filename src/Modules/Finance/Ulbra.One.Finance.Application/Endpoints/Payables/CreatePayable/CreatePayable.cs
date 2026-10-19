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
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.CreatePayable;

public sealed class CreatePayable : IEndpoint
{
    public sealed record InstallmentInput(
        [property: SwaggerSchema("When this installment is due.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly DueDate,
        [property: SwaggerSchema("Installment amount in BRL. The payable's total is the sum of its installments.")]
        [property: SwaggerExample("1250.00")]
        decimal Amount,
        [property: SwaggerSchema("Boleto linha digitavel, when this installment is paid by boleto.")]
        string? BoletoDigitableLine = null,
        [property: SwaggerSchema("Per-payment PIX key override (AEL-938), when paid by PIX: overrides the "
            + "vendor's registered key for this payment only. Blank uses the vendor's key.")]
        string? PixKeyOverride = null,
        [property: SwaggerSchema("Per-payment PIX \"copia e cola\" (EMV/BR Code, AEL-963), when paid by a PIX "
            + "QR: a static QR decodes to a key, a dynamic QR is sent as its payload URL on the CNAB forma 47 "
            + "remittance. Mutually exclusive with PixKeyOverride.")]
        string? PixPayloadEmv = null);

    public sealed record GenerateInput(
        [property: SwaggerSchema("Total to split across the generated installments, in BRL. The remainder "
            + "of an uneven division falls on the last one.")]
        [property: SwaggerExample("12000.00")]
        decimal TotalAmount,
        [property: SwaggerSchema("How many installments to generate.")]
        [property: SwaggerExample("12")]
        int Count,
        [property: SwaggerSchema("Due date of the first installment; the rest follow at IntervalMonths spacing.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly FirstDueDate,
        [property: SwaggerSchema("Months between installments. 1 gives a monthly schedule.")]
        [property: SwaggerExample("1")]
        int IntervalMonths = 1);

    public sealed record Request
    {
        [SwaggerSchema("Company that owes the payable.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Vendor to be paid. Required; must reference an existing vendor (vendors are global to the organization).")]
        public VendorId VendorId { get; set; }

        [SwaggerSchema("Cost center the expense is charged to.")]
        public CostCenterId CostCenterId { get; set; }

        // Optional unit (estabelecimento) tag for accounting tracking. When set, must belong to the company.
        [SwaggerSchema("Unit (estabelecimento) tag for accounting tracking. Optional; when set it must "
            + "belong to the same company.")]
        public UnitId? UnitId { get; set; }

        [SwaggerSchema("Expense category. Optional at creation — classification can come later, and is "
            + "what the accounting posting needs.")]
        public ExpenseCategoryId? CategoryId { get; set; }

        [SwaggerSchema("Where the payable came from. Leave the default for one typed in by hand; the "
            + "other values are stamped by the flows that generate payables automatically.")]
        public PayableOrigin Origin { get; set; } = PayableOrigin.Manual;

        [SwaggerSchema("What is being paid for.")]
        [SwaggerExample("Manutencao preventiva do ar-condicionado - agosto/2026")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Number of the vendor's document (nota fiscal, fatura or contract).")]
        [SwaggerExample("NF 12345")]
        [NormalizeText] public string? DocumentNumber { get; set; }

        [SwaggerSchema("Issue date of the document. It is the accounting competency anchor, so a payable "
            + "without one has no competency yet and drops out of competency-filtered lists.")]
        [SwaggerExample("2026-08-31")]
        public DateOnly? IssueDate { get; set; }

        [SwaggerSchema("The installment schedule, authored one by one. Send either this or Generate, not "
            + "both.")]
        public List<InstallmentInput>? Installments { get; set; }

        [SwaggerSchema("Ask the server to build the schedule from a total, a count and a first due date. "
            + "The alternative to Installments.")]
        public GenerateInput? Generate { get; set; }

        [SwaggerSchema("Expense ledger account for the accounting posting. Optional here; it can be set "
            + "later by classifying the payable.")]
        public LedgerAccountId? AccountingAccountId { get; set; }

        [SwaggerSchema("Bank account the payment will come out of. Omit to use the company's default at "
            + "payment time.")]
        public BankAccountId? PaymentBankAccountId { get; set; }

        [SwaggerSchema("How this payable is paid. Omit to inherit the paying account's default method — "
            + "the payment queue filters on that EFFECTIVE method, not on this field alone.")]
        public PaymentMethod? PaymentMethod { get; set; }

        // Optional type for a manual payable (no fiscal document): Service = subject to source withholding.
        // When a document is later uploaded, its format (NFS-e/NF-e) authoritatively sets this.
        [SwaggerSchema("Service or product. It decides whether source withholdings apply, so it matters "
            + "fiscally. Only meaningful for a manual payable: once a fiscal document is uploaded, its "
            + "format (NFS-e or NF-e) sets this authoritatively and overrides whatever was sent here.")]
        public PayableType? Type { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle).WithName("CreatePayable").WithTags("Finance")
            .WithSummary("Creates a payable.")
            .WithDescription("Registers an account payable with its installment schedule. Give the "
                + "schedule one of two ways: Installments to author each one, or Generate to split a "
                + "total across N installments — sending both is rejected. Classification (category and "
                + "ledger account) is optional here and can be completed later; the payable still has to "
                + "be classified before it can be posted to accounting.")
            .RequirePermission(Permissions.PayablesWrite).WithRequestNormalizer<Request>()
            .Produces<PayableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        IPublishEndpoint publish,
        TimeProvider clock,
        ChargeReferenceDuplicateChecker chargeReferences,
        CancellationToken ct)
    {
        // Clients may only send the intake origins (Manual, FiscalDocument, Advance); the others are
        // reserved for system integrations (purchase orders, payroll, Senior import).
        if (req.Origin is PayableOrigin.Purchase or PayableOrigin.Payroll or PayableOrigin.SeniorImport)
            return ErrorResults.InvalidRequest("Origin is reserved for system integrations.", ErrorCodes.Payable.OriginInvalid);

        var companyId = req.CompanyId.Value;

        var cc = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == req.CostCenterId.Value && c.CompanyId == companyId, ct);
        if (cc is null) return ErrorResults.InvalidRequest("Cost center is invalid for this company.", ErrorCodes.Payable.CostCenterInvalid);

        var vendor = await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == req.VendorId.Value, ct);
        if (vendor is null) return ErrorResults.InvalidRequest("Vendor not found.", ErrorCodes.Vendor.NotFound);

        if (req.UnitId is { } unitId)
        {
            var unitOk = await uow.ReadSet<Unit>().AnyAsync(u => u.Id == unitId.Value && u.CompanyId == companyId, ct);
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
        else
        {
            return ErrorResults.InvalidRequest("Provide either explicit installments or a generation spec.", ErrorCodes.Payable.InstallmentsInvalid);
        }

        // Shift-left the boleto check (AEL-932): a provided linha digitável must be well-formed here instead
        // of silently dropping out at remittance time. A blank line stays allowed (fill-the-barcode-later).
        if (BoletoEntryValidation.Problem(installments.Select(i => i.BoletoDigitableLine).ToList()) is { } boletoProblem)
            return boletoProblem;
        // Shift-left the copia-e-cola check (AEL-963): a provided payload must be a valid EMV/BR Code (structure
        // + CRC) here, instead of silently dropping out at remittance time.
        if (PixPayloadEmvEntryValidation.Problem(installments.Select(i => (i.PixKeyOverride, i.PixPayloadEmv)).ToList()) is { } pixCpProblem)
            return pixCpProblem;

        // Same boleto, paid twice: the line may already sit on another title or on a requisition plan.
        if (await chargeReferences.ProblemAsync(ChargeReferenceKind.BoletoLine, installments.Select(i => i.BoletoDigitableLine).ToList(), [],
                ErrorCodes.Payable.BoletoDigitableLineDuplicate, ct) is { } duplicateProblem)
            return duplicateProblem;

        // Um PIX Copia e Cola é uma cobrança emitida, como o boleto (AEL-963).
        if (await chargeReferences.ProblemAsync(ChargeReferenceKind.PixPayloadEmv,
                installments.Select(i => i.PixPayloadEmv).ToList(), [],
                ErrorCodes.Payable.PixPayloadEmvDuplicate, ct) is { } pixDuplicate)
            return pixDuplicate;

        var payable = new Payable(companyId, req.Origin, vendor.Id, vendor.Name, req.DocumentNumber, req.IssueDate,
            req.Description, cc.Id, req.CategoryId?.Value, installments, req.UnitId?.Value);

        if (req.AccountingAccountId is not null)
        {
            var err = await ClassificationValidation.ValidateAsync(uow, req.AccountingAccountId?.Value, ct);
            if (err is not null) return ErrorResults.InvalidRequest("Invalid chart account.", err);
            payable.Classify(req.AccountingAccountId?.Value);
        }

        payable.SetPaymentSource(req.PaymentBankAccountId?.Value, req.PaymentMethod);
        if (req.Type is not null) payable.SetType(req.Type);

        // Apply vendor payment schedule to each installment's scheduled payment date.
        var schedule = vendor.PaymentSchedule ?? PaymentSchedule.Default;
        foreach (var inst in payable.Installments)
            inst.SetScheduledPaymentDate(schedule.Resolve(inst.DueDate));

        // Payable stays at Received; the consumer (PayableReceivedConsumer) handles auto-approval.
        await uow.AddAsync(payable, ct);
        await publish.Publish(new PayableReceived(payable.Id, companyId), ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/payables/{new PayableId(payable.Id)}", PayableResponse.From(payable));
    }
}
