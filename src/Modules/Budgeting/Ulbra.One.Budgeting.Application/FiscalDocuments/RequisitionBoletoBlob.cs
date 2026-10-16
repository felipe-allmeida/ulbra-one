namespace Ulbra.One.Budgeting.Application.FiscalDocuments;

/// <summary>Storage key root for requisition boleto uploads. Combined with the company id by
/// <c>ScopedBlobKey</c> it namespaces each upload per-company (AEL-764 IDOR guard).</summary>
public static class RequisitionBoletoBlob
{
    public const string KeyRoot = "requisition-boleto";
}
