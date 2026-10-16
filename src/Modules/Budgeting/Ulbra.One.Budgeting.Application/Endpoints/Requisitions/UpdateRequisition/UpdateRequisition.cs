using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UpdateRequisition;

/// <summary>Updates the editable fields of a requisition (dimensions, code and status are immutable).
/// Allowed only while Draft or sent back for Complement — otherwise 409.</summary>
public sealed class UpdateRequisition : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Requisition type. A recurring type (Conta Fixa) is what makes EndCompetency and the "
            + "payment frequency meaningful.")]
        public RequisitionType Type { get; set; }

        [SwaggerSchema("What is being requested. Required.")]
        [SwaggerExample("Preventive maintenance of the lab air conditioning")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Extra context for triage and the approver.")]
        [NormalizeText] public string? Observation { get; set; }

        [SwaggerSchema("Estimated total, in BRL.")]
        [SwaggerExample("12500.00")]
        public decimal EstimateValue { get; set; }

        [SwaggerSchema("How to read EstimateValue: as the total for the whole window, or as the amount per "
            + "month.")]
        public EstimateType EstimateType { get; set; }

        [SwaggerSchema("First competency the spend lands in (`YYYY-MM`). Required.")]
        [SwaggerExample("2026-08")]
        [NormalizeText] public string StartCompetency { get; set; } = string.Empty;

        [SwaggerSchema("Last competency of the window (`YYYY-MM`), inclusive. Omit for an open-ended "
            + "recurrence.")]
        [SwaggerExample("2026-12")]
        [NormalizeText] public string? EndCompetency { get; set; }

        [SwaggerSchema("How often the payment repeats over the window.")]
        public PaymentFrequency? PaymentFrequency { get; set; }

        [SwaggerSchema("How many installments the payment is split into.")]
        [SwaggerExample("12")]
        public int? PaymentInstallmentsCount { get; set; }

        /// <summary>Vendor to be paid (fornecedor) — required; must reference an existing vendor (vendors are global).</summary>
        [SwaggerSchema("Vendor to be paid. Required; must reference an existing vendor (vendors are global to the organization).")]
        public VendorId VendorId { get; set; }

        /// <summary>First installment due date — required; anchors the generated Payable's schedule (AEL-793).</summary>
        [SwaggerSchema("Due date of the first installment. Required — it anchors the schedule of the "
            + "Payable that Finance generates on approval.")]
        [SwaggerExample("2026-08-10")]
        public DateOnly FirstDueDate { get; set; }

        /// <summary>Project link (AEL-792). <c>null</c> clears it.</summary>
        [SwaggerSchema("Project whose accumulated ceiling this requisition consumes. Send null to unlink it.")]
        public ProjectId? ProjectId { get; set; }

        /// <summary>Optional front-loaded emission date. See <see cref="Installments"/>.</summary>
        [SwaggerSchema("Optional emission date informed already at creation (front-load). Send with "
            + "Installments to keep the plan; omit both to clear a previously front-loaded plan.")]
        [SwaggerExample("2026-08-01")]
        public DateOnly? IssueDate { get; set; }

        /// <summary>Optional front-loaded installment plan. Present → replaces the stored plan (its sum
        /// overwrites <see cref="EstimateValue"/>); absent → clears any previously front-loaded plan.</summary>
        [SwaggerSchema("Optional installment plan (front-load). Present replaces the stored plan; omit both "
            + "this and IssueDate to clear it and fill the financial part later.")]
        public IReadOnlyList<FinancialComplementInstallment>? Installments { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateRequisition")
            .WithTags("Budgeting")
            .WithSummary("Updates a requisition.")
            .WithDescription("Replaces the editable fields of a requisition. Its dimensions (unit, cost "
                + "center), code and status are immutable, and editing is only allowed while it is a Draft "
                + "or has been sent back for complement — any other state returns 409. This is a full "
                + "replace, not a patch.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        RequisitionId id,
        Request req,
        IVendorLookup vendors,
        UserContext user,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Attachments)
            .Include(r => r.Installments) // tracked so a replace/clear of the front-loaded plan orphan-deletes
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        // Scoped requester (AEL-883): may edit only within its Empresa × Unidade × Centro de Custo scope.
        if (!user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
            return ErrorResults.Forbidden("You cannot edit requisitions of this unit / cost center.", ErrorCodes.Requisition.ScopeForbidden);

        var invalid = RequisitionValidation.Validate(req.Type, req.EstimateType, req.Description, req.EstimateValue, req.StartCompetency, req.EndCompetency, req.PaymentInstallmentsCount);
        if (invalid is not null) return invalid;

        // Optional front-loaded plan: present → replace, absent → clear. Draft stores a wrong-length boleto line;
        // enforced at Submit (see CreateRequisition).
        if (!FinancialPlanValidation.TryBuild(req.IssueDate, req.Installments, required: false, out var plan, out var planError, enforceBoletoLine: false))
            return planError!;

        if (!await vendors.ExistsAsync(req.VendorId.Value, ct))
            return ErrorResults.InvalidRequest("Vendor not found.", ErrorCodes.Vendor.NotFound);

        if (req.ProjectId is { } projectId)
        {
            var project = await uow.ReadSet<Project>().FirstOrDefaultAsync(p => p.Id == projectId.Value, ct);
            if (project is null)
                return ErrorResults.InvalidRequest("Project not found.", ErrorCodes.Requisition.ProjectNotFound);
            if (project.CompanyId != requisition.CompanyId)
                return ErrorResults.InvalidRequest("Project belongs to a different company.", ErrorCodes.Requisition.ProjectCompanyMismatch);
        }

        try
        {
            requisition.Update(req.Type, req.Description, req.EstimateValue, req.EstimateType,
                req.StartCompetency, req.VendorId.Value, req.FirstDueDate, req.EndCompetency, req.Observation,
                req.PaymentFrequency, req.PaymentInstallmentsCount);

            if (req.ProjectId is { } linked) requisition.LinkToProject(linked.Value);
            else requisition.UnlinkProject();

            // Update ran SetDetails first (it fixes the typed estimate and drops the plan on a vendor
            // swap). Now apply the request's plan: present → replace; absent → clear (the form is front-load-aware
            // and always sends the current subform state).
            if (plan is not null) requisition.SetFinancialPlan(req.IssueDate, plan);
            else requisition.ClearFinancialPlan();
        }
        catch (RequisitionException ex)
        {
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        // Freshly authored installments are appended to a now-Modified parent, so mark them Added explicitly —
        // else EF's store-generated-key heuristic marks the set-key children Modified → 0-row update (same reason
        // the financial-complement submit AddAsyncs them). Replaced/cleared rows orphan-delete via the tracked
        // collection loaded above.
        foreach (var installment in requisition.Installments) await uow.AddAsync(installment, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
