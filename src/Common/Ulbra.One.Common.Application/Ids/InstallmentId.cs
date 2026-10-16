namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Installment entities (wire form <c>installment_{guid:N}</c>).</summary>
[PrefixedId("installment_")]
public readonly partial record struct InstallmentId(Guid Value);
