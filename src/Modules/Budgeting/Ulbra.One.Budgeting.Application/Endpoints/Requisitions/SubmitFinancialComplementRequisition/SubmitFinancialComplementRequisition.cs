using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SubmitFinancialComplementRequisition;

/// <summary>Requester submits the financial complement (AEL-844): the authored installment plan (due date,
/// value to transfer and payment method per installment) plus the single emission date. The domain persists the
/// plan, sets the requisition's value to their sum (the "programmed total"), and moves it to Approval — or back to
/// Triage when that total rose above the triage-vetted amount or the vendor changed. Fiscal documents are
/// optional and uploaded via the attachment endpoint (no NF-required validation here — AEL-841).</summary>
public sealed class SubmitFinancialComplementRequisition : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Single emission date for the whole plan — the accounting competency "
            + "anchor of the Payable that Finance generates.")]
        [property: SwaggerExample("2026-08-01")]
        DateOnly? IssueDate,
        [property: SwaggerSchema("The authored installment plan. Its sum replaces the requisition's "
            + "estimated value as the programmed total.")]
        IReadOnlyList<FinancialComplementInstallment> Installments);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/submit-financial-complement", Handle)
            .WithName("SubmitFinancialComplementRequisition").WithTags("Budgeting")
            .WithSummary("Submits a requisition's financial complement.")
            .WithDescription("The requester authors the installment plan — due date, value and payment "
                + "method per installment — plus one emission date. The plan is persisted, the "
                + "requisition's value becomes the installments' sum (its programmed total), and it moves "
                + "to Approval. It goes back to Triage instead when that total rose above the "
                + "triage-vetted amount or the vendor changed. Fiscal documents are optional here and are "
                + "uploaded through the attachment endpoint.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, Request req, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (!FinancialPlanValidation.TryBuild(req.IssueDate, req.Installments, required: true, out var plan, out var error))
            return error!;

        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Installments)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; keep the endpoint's 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        try
        {
            requisition.SubmitFinancialComplement(Guid.Parse(user.UserId), clock.GetUtcNow(), req.IssueDate, plan!);
        }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        // Freshly authored installments are appended to a now-Modified parent, so mark them Added explicitly —
        // else EF's store-generated-key heuristic marks the set-key children Modified → 0-row update (the same
        // reason the transition is AddAsync'd). Replaced installments (re-submit) orphan-delete via the tracked
        // collection.
        foreach (var installment in requisition.Installments) await uow.AddAsync(installment, ct);
        await uow.AddAsync(requisition.Transitions[^1], ct);
        await requisition.PublishLastTransition(publish, ct); // notify the approver / triagem (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
