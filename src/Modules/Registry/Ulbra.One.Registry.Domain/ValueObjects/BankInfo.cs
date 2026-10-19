namespace Ulbra.One.Registry.Domain.ValueObjects;

/// <summary>Vendor bank coordinates as a value object (EF owned type). Immutable. All-empty = no bank data.
/// These are exactly the fields the CNAB240 remessa generator reads from the vendor.</summary>
public sealed record BankInfo(
    string BankCode,
    string Branch,
    string Account,
    string AccountDigit,
    string PixKey)
{
    public static BankInfo Empty => new("", "", "", "", "");
}
