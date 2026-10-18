using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints;

/// <summary>Inbox row. <see cref="ReferenceId"/> is emitted already in the originating aggregate's typed-id
/// wire form (<c>req_…</c> / <c>adeq_…</c>, derived from <see cref="Type"/>) so the frontend can deep-link
/// straight to the entity without knowing the mapping.</summary>
[SwaggerSchema("One row of the in-app inbox.")]
public sealed record NotificationResponse(
    NotificationId Id,
    [property: SwaggerSchema("What the notification is about, as a stable key. It also decides how "
        + "ReferenceId is shaped.")]
    [property: SwaggerExample("RequisitionApproved")]
    string Type,
    [property: SwaggerSchema("Headline, already in pt-BR — notifications are composed server-side rather "
        + "than translated by the client.")]
    [property: SwaggerExample("Requisição #482 aprovada")]
    string Title,
    [property: SwaggerSchema("Body text, already in pt-BR.")]
    [property: SwaggerExample("Sua requisição de manutenção predial foi aprovada e seguiu para pagamento.")]
    string Message,
    [property: SwaggerSchema("The entity the notification points at, already in its typed-id wire form "
        + "(`req_…`, `adeq_…`) derived from Type — so the client can deep-link without knowing the "
        + "mapping. Null when the notification refers to nothing in particular.")]
    string? ReferenceId,
    [property: SwaggerSchema("When the event that produced it happened.")]
    DateTimeOffset OccurredAt,
    [property: SwaggerSchema("When the user read it. Null means unread — that is what the badge counts.")]
    DateTimeOffset? ReadAt)
{
    public static NotificationResponse From(Notification n) =>
        new(new NotificationId(n.Id), n.Type, n.Title, n.Message, WireReference(n), n.OccurredAt, n.ReadAt);

    private static string? WireReference(Notification n)
    {
        if (n.ReferenceId is not Guid reference) return null;
        if (n.Type.StartsWith("Requisition", StringComparison.Ordinal)) return new RequisitionId(reference).ToString();
        if (n.Type.StartsWith("BudgetAdjustment", StringComparison.Ordinal)) return new BudgetAdjustmentId(reference).ToString();
        return reference.ToString();
    }
}
