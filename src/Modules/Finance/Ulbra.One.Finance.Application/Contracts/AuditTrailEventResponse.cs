using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>
/// One typed event on a title's audit timeline. <c>Type</c> matches the FE i18n keys under workflow.timeline.*.
/// <c>Metadata</c> carries small string extras (reason, amount, installment sequence) for display.
/// </summary>
[SwaggerSchema("One event on a title's timeline. The event type is a stable key, not a sentence — the "
    + "frontend renders it through i18n, so it must not be shown to a user as-is.")]
public sealed record AuditTrailEventResponse(
    [property: SwaggerSchema("What happened, as a stable key matching the frontend's i18n entries under "
        + "`workflow.timeline.*`.")]
    [property: SwaggerExample("payable.approved")]
    string Type,
    [property: SwaggerSchema("Who did it. Null for events the system produced on its own, such as an "
        + "auto-approval.")]
    string? ActorId,
    [property: SwaggerSchema("Display name of the actor, resolved for the timeline.")]
    [property: SwaggerExample("Maria Silva")]
    string ActorName,
    [property: SwaggerSchema("The actor's Google profile photo. Null for the system actor, for a user with "
        + "no photo, and for a user id that no longer resolves — render the initials of ActorName instead.")]
    [property: SwaggerExample("https://lh3.googleusercontent.com/a/ACg8ocK")]
    string? ActorPictureUrl,
    [property: SwaggerSchema("When it happened.")]
    DateTimeOffset Timestamp,
    [property: SwaggerSchema("Small extras for display — a reason, an amount, an installment sequence. "
        + "Which keys appear depends on Type, so read it defensively.")]
    IReadOnlyDictionary<string, string> Metadata);
