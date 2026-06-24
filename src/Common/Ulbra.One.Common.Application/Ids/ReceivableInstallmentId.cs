namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for ReceivableInstallment entities (wire form <c>receivable_installment_{guid:N}</c>).</summary>
[PrefixedId("receivable_installment_")]
public readonly partial record struct ReceivableInstallmentId(Guid Value);
