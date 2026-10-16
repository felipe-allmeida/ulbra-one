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
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.CreateRequisition;

/// <summary>Creates a Draft requisition. Validates the budget dimensions via the shared lookup (deriving
/// the company from the unit) and assigns a per-company sequential <c>Code</c> = max+1, retrying on the
/// unique-index race — the same pattern as posting a journal entry.</summary>
public sealed class CreateRequisition : IEndpoint
{
    private const int MaxAttempts = 5;

    public sealed record Request
    {
        [SwaggerSchema("Unit the spend is charged to. The company is derived from it.")]
        public UnitId UnitId { get; set; }

        [SwaggerSchema("Cost center (natureza) whose budget the requisition consumes.")]
        public CostCenterId CostCenterId { get; set; }

        [SwaggerSchema("Requisition type. A recurring type (Conta Fixa) is what makes EndCompetency and the "
            + "payment frequency meaningful.")]
        public RequisitionType Type { get; set; }

        [SwaggerSchema("What is being requested. Required — it is the headline shown throughout the "
            + "workflow and the main target of the list's free-text search.")]
        [SwaggerExample("Preventive maintenance of the lab air conditioning")]
        [NormalizeText] public string Description { get; set; } = string.Empty;

        [SwaggerSchema("Extra context for triage and the approver.")]
        [NormalizeText] public string? Observation { get; set; }

        [SwaggerSchema("Estimated total, in BRL. Provisional until the financial complement replaces it "
            + "with the sum of the authored installments.")]
        [SwaggerExample("12500.00")]
        public decimal EstimateValue { get; set; }

        [SwaggerSchema("How to read EstimateValue: as the total for the whole window, or as the amount per "
            + "month.")]
        public EstimateType EstimateType { get; set; }

        [SwaggerSchema("First competency the spend lands in (`YYYY-MM`). Required.")]
        [SwaggerExample("2026-08")]
        [NormalizeText] public string StartCompetency { get; set; } = string.Empty;

        [SwaggerSchema("Last competency of the window (`YYYY-MM`), inclusive. Omit for an open-ended "
            + "recurrence — it can be closed later through the terminate-recurrence endpoint.")]
        [SwaggerExample("2026-12")]
        [NormalizeText] public string? EndCompetency { get; set; }

        [SwaggerSchema("How often the payment repeats over the window.")]
        public PaymentFrequency? PaymentFrequency { get; set; }

        [SwaggerSchema("How many installments the payment is split into.")]
        [SwaggerExample("12")]
        public int? PaymentInstallmentsCount { get; set; }

        /// <summary>Vendor to be paid — required; must reference an existing vendor (vendors are global).</summary>
        [SwaggerSchema("Vendor to be paid. Required; must reference an existing vendor (vendors are global to the organization).")]
        public VendorId VendorId { get; set; }

        /// <summary>First installment due date — required; anchors the generated Payable's schedule (AEL-793).</summary>
        [SwaggerSchema("Due date of the first installment. Required — it anchors the schedule of the "
            + "Payable that Finance generates on approval.")]
        [SwaggerExample("2026-08-10")]
        public DateOnly FirstDueDate { get; set; }

        /// <summary>Optional project whose accumulated ceiling this requisition consumes (AEL-792).</summary>
        [SwaggerSchema("Project whose accumulated ceiling this requisition consumes. Optional; the "
            + "requisition still consumes its cost center's monthly budget either way.")]
        public ProjectId? ProjectId { get; set; }

        /// <summary>Optional front-loaded emission date. Present together with
        /// <see cref="Installments"/> when the requester informs the financial part already at creation, so
        /// triage approval skips the financial-complement step.</summary>
        [SwaggerSchema("Optional emission date informed already at creation (front-load). When present with "
            + "Installments, triage approval sends the requisition straight to the approver.")]
        [SwaggerExample("2026-08-01")]
        public DateOnly? IssueDate { get; set; }

        /// <summary>Optional front-loaded installment plan. Same shape as the financial complement; its
        /// sum overwrites <see cref="EstimateValue"/> as the programmed total.</summary>
        [SwaggerSchema("Optional installment plan informed already at creation (front-load). Its sum becomes "
            + "the requisition's value. Omit to fill it later in the financial-complement step.")]
        public IReadOnlyList<FinancialComplementInstallment>? Installments { get; set; }
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateRequisition")
            .WithTags("Budgeting")
            .WithSummary("Creates a requisition.")
            .WithDescription("Opens a requisition as a Draft and assigns it a per-company sequential code. "
                + "The requester may only create within their own Company x Unit x Cost Center scope, "
                + "fail-closed, so an out-of-scope combination returns 403. Nothing enters the workflow "
                + "until the draft is submitted.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .WithRequestNormalizer<Request>()
            .Produces<RequisitionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        Request req,
        IBudgetDimensionLookup dimensions,
        IVendorLookup vendors,
        UserContext user,
        IUnitOfWork uow,
        ChargeReferenceDuplicateChecker chargeReferences,
        CancellationToken ct)
    {
        var invalid = RequisitionValidation.Validate(req.Type, req.EstimateType, req.Description, req.EstimateValue, req.StartCompetency, req.EndCompetency, req.PaymentInstallmentsCount);
        if (invalid is not null) return invalid;

        // Optional front-loaded plan (validated once, pure, before the retry loop). Draft stores a wrong-length
        // boleto line (read boleto still attaches); the 47/48 rule is enforced at Submit, not here.
        if (!FinancialPlanValidation.TryBuild(req.IssueDate, req.Installments, required: false, out var plan, out var planError, enforceBoletoLine: false))
            return planError!;

        // A draft may still carry a wrong-length line, but never one already queued for payment elsewhere —
        // catching it at authoring is the only moment the operator can still pick the right boleto.
        if (plan is not null && await chargeReferences.ProblemAsync(ChargeReferenceKind.BoletoLine,
                plan.Select(i => i.BoletoDigitableLine).ToList(), [], ErrorCodes.Requisition.BoletoDigitableLineDuplicate, ct) is { } duplicate)
            return duplicate;

        // Um QR dinâmico é uma cobrança emitida, como o boleto (AEL-963); o estático é endereço e repete.
        if (plan is not null && await chargeReferences.ProblemAsync(ChargeReferenceKind.PixPayloadEmv,
                plan.Select(i => i.PixPayloadEmv).ToList(), [],
                ErrorCodes.Requisition.PixPayloadEmvDuplicate, ct) is { } pixDuplicate)
            return pixDuplicate;

        var dimension = await dimensions.ResolveAsync(req.UnitId.Value, req.CostCenterId.Value, ct);
        if (!dimension.IsValid)
            return ErrorResults.InvalidRequest(DimensionDetail(dimension.Status), DimensionTitle(dimension.Status));

        // Scoped requester (AEL-883): may create only within its Empresa × Unidade × Centro de Custo scope
        // (explicit-list intersection). Admin and non-scoped roles bypass; fail-closed for Requester/Approver.
        if (!user.CanAccessScope(dimension.CompanyId, req.UnitId.Value, req.CostCenterId.Value))
            return ErrorResults.Forbidden("You cannot create requisitions for this unit / cost center.", ErrorCodes.Requisition.ScopeForbidden);

        if (!await vendors.ExistsAsync(req.VendorId.Value, ct))
            return ErrorResults.InvalidRequest("Vendor not found.", ErrorCodes.Vendor.NotFound);

        // Conta Fixa is unique per (vendor, unit, natureza=CostCenter) while ACTIVE (AEL-881) — there cannot
        // be two "electricity bills" for the same unit. Friendly pre-check; the partial index is the race backstop.
        if (req.Type == RequisitionType.FixedExpense && await ActiveFixedExpenseExistsAsync(uow, req, ct))
            return ErrorResults.InvalidState(RequisitionErrors.FixedExpenseDuplicate.Description, RequisitionErrors.FixedExpenseDuplicate.Code);

        if (req.ProjectId is { } projectId)
        {
            var project = await uow.ReadSet<Project>().FirstOrDefaultAsync(p => p.Id == projectId.Value, ct);
            if (project is null)
                return ErrorResults.InvalidRequest("Project not found.", ErrorCodes.Requisition.ProjectNotFound);
            if (project.CompanyId != dimension.CompanyId)
                return ErrorResults.InvalidRequest("Project belongs to a different company.", ErrorCodes.Requisition.ProjectCompanyMismatch);
            if (project.Status != ProjectStatus.Active)
                return ErrorResults.InvalidRequest("Project is not active.", ErrorCodes.Requisition.ProjectInactive);
        }

        for (var attempt = 0; ; attempt++)
        {
            var nextCode = (await uow.ReadSet<Requisition>()
                .Where(r => r.CompanyId == dimension.CompanyId)
                .MaxAsync(r => (int?)r.Code, ct) ?? 0) + 1;

            var requisition = new Requisition(
                dimension.CompanyId, req.UnitId.Value, req.CostCenterId.Value, nextCode,
                req.Type, req.Description, req.EstimateValue, req.EstimateType,
                req.StartCompetency, req.VendorId.Value, req.FirstDueDate, req.EndCompetency, req.Observation,
                req.PaymentFrequency, req.PaymentInstallmentsCount);

            if (req.ProjectId is { } linkedProject) requisition.LinkToProject(linkedProject.Value);

            // Front-load: store the authored plan on the still-Draft requisition. Its installments are
            // reachable from the not-yet-tracked root, so the graph add below marks them Added — no per-child
            // AddAsync needed (unlike the financial-complement submit, where the parent is already Modified).
            if (plan is not null)
            {
                try { requisition.SetFinancialPlan(req.IssueDate, plan); }
                catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }
            }

            await uow.AddAsync(requisition, ct);
            try
            {
                await uow.SaveChangesAsync(ct);
                return TypedResults.Created($"/api/requisitions/{new RequisitionId(requisition.Id)}", RequisitionResponse.From(requisition));
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Lost the race for this Code against a concurrent create. Clear the tracker so the next
                // iteration re-computes max+1 with a fresh entity.
                uow.ClearChangeTracker();
                // A concurrent create may instead have taken the (vendor, unit, natureza) key of an active
                // Conta Fixa (the partial unique index fired). Re-check and surface the duplicate 409 instead of
                // looping on a constraint that a retry can never satisfy (AEL-881).
                if (req.Type == RequisitionType.FixedExpense && await ActiveFixedExpenseExistsAsync(uow, req, ct))
                    return ErrorResults.InvalidState(RequisitionErrors.FixedExpenseDuplicate.Description, RequisitionErrors.FixedExpenseDuplicate.Code);
            }
        }
    }

    // Conta Fixa is unique per (VendorId, UnitId, CostCenterId) while active — active = not Cancelled and not
    // Finalized (AEL-881). Mirrors the partial unique index ix_requisitions_fixed_expense_unique.
    private static Task<bool> ActiveFixedExpenseExistsAsync(IUnitOfWork uow, Request req, CancellationToken ct) =>
        uow.ReadSet<Requisition>().AnyAsync(r =>
            r.Type == RequisitionType.FixedExpense
            && r.VendorId == req.VendorId.Value
            && r.UnitId == req.UnitId.Value
            && r.CostCenterId == req.CostCenterId.Value
            && r.Status != RequisitionStatus.Cancelled
            && r.Status != RequisitionStatus.Finalized, ct);

    // Distinct, stable title codes per dimension failure so the frontend can localize a specific message.
    private static string DimensionTitle(DimensionStatus status) => status switch
    {
        DimensionStatus.UnitNotFound => ErrorCodes.Requisition.UnitNotFound,
        DimensionStatus.CostCenterNotFound => ErrorCodes.Requisition.CostCenterNotFound,
        DimensionStatus.CompanyMismatch => ErrorCodes.Requisition.CompanyMismatch,
        DimensionStatus.CostCenterInactive => ErrorCodes.Requisition.CostCenterInactive,
        DimensionStatus.UnitNotInCostCenter => ErrorCodes.Requisition.UnitNotInCostCenter,
        _ => ErrorCodes.Requisition.DimensionsInvalid,
    };

    private static string DimensionDetail(DimensionStatus status) => status switch
    {
        DimensionStatus.UnitNotFound => "Unit not found.",
        DimensionStatus.CostCenterNotFound => "Cost center not found.",
        DimensionStatus.CompanyMismatch => "Unit and cost center belong to different companies.",
        DimensionStatus.CostCenterInactive => "Cost center is inactive.",
        DimensionStatus.UnitNotInCostCenter => "Unit is not associated with the selected cost center.",
        _ => "Invalid budget dimensions.",
    };
}
