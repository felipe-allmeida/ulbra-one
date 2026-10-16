using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>One row of the operational Contas a Pagar list = one installment with its payable context.</summary>
[SwaggerSchema("One row of the Contas a Pagar list. The grain is the INSTALLMENT, not the payable — a "
    + "title with three installments produces three rows, each carrying its payable's context.")]
public sealed record PayableListItemResponse(
    PayableId PayableId,
    InstallmentId InstallmentId,
    [property: SwaggerSchema("Position of this installment within the payable, starting at 1.")]
    [property: SwaggerExample("1")]
    int Sequence,
    [property: SwaggerSchema("How many installments the payable has in total.")]
    [property: SwaggerExample("3")]
    int InstallmentCount,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("What the payable is for.")]
    [property: SwaggerExample("Manutenção preventiva do ar-condicionado — agosto")]
    string Description,
    [property: SwaggerSchema("Cost center the spend is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Expense category, when one was assigned.")]
    ExpenseCategoryId? CategoryId,
    [property: SwaggerSchema("When this installment falls due.")]
    [property: SwaggerExample("2026-08-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Face value of the installment, in BRL, before withholdings.")]
    [property: SwaggerExample("1500.00")]
    decimal Amount,
    /// <summary>Installment amount net of the payable's withholdings (Σ NetAmount == the payable's NetTotal;
    /// equals Amount when there are none). This is what the payment esteira actually pays.</summary>
    [property: SwaggerSchema("What actually gets paid, in BRL: the installment net of its share of the "
        + "payable's withholdings. Equals Amount when there are none.")]
    [property: SwaggerExample("1430.25")]
    decimal NetAmount,
    [property: SwaggerSchema("Where this installment stands.")]
    InstallmentStatus InstallmentStatus,
    [property: SwaggerSchema("Where the whole payable stands — it can differ from the installment's own "
        + "status while other installments are still open.")]
    PayableStatus PayableStatus,
    /// <summary>Date the daily sweep will process this installment (defaults to DueDate at creation).</summary>
    [property: SwaggerSchema("When the daily sweep will pick this installment up. Defaults to DueDate "
        + "at creation and can be moved without changing what was agreed with the vendor.")]
    [property: SwaggerExample("2026-08-08")]
    DateOnly ScheduledPaymentDate,
    /// <summary>True when this installment has been manually paused from the remittance sweep.</summary>
    [property: SwaggerSchema("Whether the installment is paused from the sweep.")]
    bool IsHeld,
    /// <summary>The reason provided when holding this installment, or null if not held.</summary>
    [property: SwaggerSchema("Why it is held. Null when it is not.")]
    string? HoldReason,
    /// <summary>Id of the remittance this installment belongs to, or null if not in a remittance.</summary>
    [property: SwaggerSchema("Remittance the installment is in, if any. While it sits in a pending "
        + "remittance it cannot be paid by hand.")]
    RemittanceId? RemittanceId,
    /// <summary>1-based sequence number of this installment within its remittance, or null if not in a remittance.</summary>
    [property: SwaggerSchema("Position of this installment inside that remittance file.")]
    [property: SwaggerExample("42")]
    int? RemittanceSequence,
    /// <summary>"Remittance" when the installment can ride the automatic CNAB rail; "Manual" otherwise.</summary>
    [property: SwaggerSchema("`Remittance` when the installment can ride the automatic CNAB rail, "
        + "`Manual` otherwise. Computed live, so it reflects the vendor and account data as they are now.")]
    [property: SwaggerExample("Remittance")]
    string Rail,
    /// <summary>The reason the installment cannot use the remittance rail, or null when Rail is "Remittance".</summary>
    [property: SwaggerSchema("Why the rail is Manual. Null when it is not — this is the field that "
        + "explains a disabled checkbox in the queue.")]
    [property: SwaggerExample("BankNotSupported")]
    string? ManualReason,
    // ── appended for the payment-queue redesign (additive on the wire; do not reorder) ──
    /// <summary>Vendor CNPJ/CPF digits, resolved live from Registry (like Rail), or null if the vendor is gone.</summary>
    [property: SwaggerSchema("Vendor CNPJ or CPF, digits only, resolved live from the registry. Null "
        + "when the vendor no longer exists there.")]
    [property: SwaggerExample("12345678000195")]
    string? VendorDocument,
    VendorId VendorId,
    /// <summary>Method a remittance would actually use: <c>Payable.PaymentMethod ?? paying-account default</c>. Null = none configured.</summary>
    [property: SwaggerSchema("The method a remittance would actually use: the payable's own, falling "
        + "back to the paying account's default. Null means neither is configured, which is itself a "
        + "reason the installment cannot be remitted.")]
    PaymentMethod? EffectiveMethod,
    [property: SwaggerSchema("Boleto linha digitável. Null unless paid by boleto.")]
    string? BoletoDigitableLine,
    [property: SwaggerSchema("Invoice number of the payable, when one is registered.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Emission date of the invoice.")]
    [property: SwaggerExample("2026-08-05")]
    DateOnly? IssueDate,
    [property: SwaggerSchema("Unit attributed to the payable, for accounting tracking.")]
    UnitId? OriginUnitId,
    /// <summary>False on a Scheduled payable means "NF pendente" (scheduled without a fiscal document).</summary>
    [property: SwaggerSchema("Whether a Nota Fiscal is registered. FALSE on a scheduled payable is the "
        + "\"NF pendente\" condition the treasury screen flags.")]
    bool HasFiscalDocument);
