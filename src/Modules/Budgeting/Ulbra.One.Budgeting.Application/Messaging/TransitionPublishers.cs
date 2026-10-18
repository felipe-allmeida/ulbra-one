using MassTransit;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Resolves the participants of a transition into the loose Guids the transition events carry.</summary>
internal static class TransitionParticipants
{
    /// <summary>The solicitante id derived from an aggregate's <c>CreatedBy</c> audit field. That field holds
    /// the authenticated user's Guid as a string, or the literal <c>"system"</c> for non-request writes — the
    /// latter (and an empty Guid) resolve to <c>null</c> so the consumer skips personal notifications that
    /// have no real recipient.</summary>
    public static Guid? RequesterFrom(string? createdBy) =>
        Guid.TryParse(createdBy, out var id) && id != Guid.Empty ? id : null;
}

/// <summary>Publishes the generic transition events consumed by the Notifications module. Publish rides the EF
/// bus outbox, so it commits atomically with the caller's <c>SaveChangesAsync</c> — invoke it right after
/// appending the transition row and before the save (same pattern as
/// <see cref="RequisitionApprovalPublisher"/>).</summary>
public static class TransitionPublishers
{
    public static Task PublishLastTransition(this Requisition requisition, IPublishEndpoint publish, CancellationToken ct) =>
        publish.Publish(RequisitionTransitioned.From(requisition), ct);

    public static Task PublishLastTransition(this BudgetAdjustment budgetAdjustment, IPublishEndpoint publish, CancellationToken ct) =>
        publish.Publish(BudgetAdjustmentTransitioned.From(budgetAdjustment), ct);

    /// <summary>Publishes the synthetic creation event for a just-created adequação (born in AwaitingTriage
    /// with no transition row). Must be called INSIDE the create endpoint's retry loop — the loop's
    /// <c>ClearChangeTracker</c> on a Code collision would otherwise discard the outbox row of a publish done
    /// once before the loop.</summary>
    public static Task PublishCreated(this BudgetAdjustment budgetAdjustment, IPublishEndpoint publish, DateTimeOffset occurredAt, CancellationToken ct) =>
        publish.Publish(BudgetAdjustmentTransitioned.ForCreation(budgetAdjustment, occurredAt), ct);
}
