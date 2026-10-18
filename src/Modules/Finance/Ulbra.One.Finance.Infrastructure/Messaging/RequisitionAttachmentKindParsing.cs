using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Maps the neutral attachment-kind string carried across the module boundary (Budgeting's
/// RequisitionAttachmentKind) to the Finance <see cref="PayableAttachmentKind"/>; unknown/legacy values fall
/// back to <see cref="PayableAttachmentKind.Other"/>. Shared by the three requisition→payable consumers.</summary>
internal static class RequisitionAttachmentKindParsing
{
    public static PayableAttachmentKind Parse(string? value) =>
        Enum.TryParse<PayableAttachmentKind>(value, ignoreCase: true, out var kind) ? kind : PayableAttachmentKind.Other;
}
