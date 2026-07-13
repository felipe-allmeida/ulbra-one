namespace Ulbra.One.Common.Domain;

/// <summary>Payment method shared across modules: how a payable is settled (Finance — Payable/Payment/BankAccount)
/// and the set a vendor declares as accepted (Registry). Int-backed and persisted as int on Payable —
/// append only, never reorder.</summary>
public enum PaymentMethod { Pix, Ted, Boleto, Cash, DirectDebit, Other }
