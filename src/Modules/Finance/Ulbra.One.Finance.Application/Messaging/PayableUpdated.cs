namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>Published on every successful payable edit (PUT /api/payables/{id});
/// the withholding-recalculation consumer reacts to it.</summary>
public sealed record PayableUpdated(Guid PayableId, Guid CompanyId);
