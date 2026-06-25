namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>Published when a payable is created; the AI auto-classify consumer reacts to it.</summary>
public sealed record PayableReceived(Guid PayableId, Guid CompanyId);
