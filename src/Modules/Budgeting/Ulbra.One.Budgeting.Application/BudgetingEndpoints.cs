using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ApproveRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ApproveTriageRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.BudgetControl;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.CancelRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.CreateRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DeleteAttachment;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DeleteRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DownloadAttachment;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.GetRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListMyDecisions;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListMyTriageDecisions;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ListRequisitions;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ResubmitRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ReturnRequisitionForComplement;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ReturnRequisitionToFinancialComplement;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SubmitFinancialComplementRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ParseBoleto;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DecodePixPayloadEmv;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.RegisterAttachment;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.RequestBoletoUploadUrl;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.AddRecurrenceCycle;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.TerminateRecurrence;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SlaReport;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.StageCounts;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SubmitRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UpdateRequisition;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UpdateAttachment;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions.UploadAttachment;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.CreateProject;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.DeleteProject;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProject;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProjectMetrics;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.GetProjectMetricsList;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.ListProjects;
using Ulbra.One.Budgeting.Application.Endpoints.Projects.UpdateProject;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ApproveBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.CancelBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.CreateBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.DeleteBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.GetBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ListBudgetAdjustments;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.ReverseBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.SendToApprovalBudgetAdjustment;
using Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments.UpdateBudgetAdjustment;
using Ulbra.One.Common.Application.Extensions;

namespace Ulbra.One.Budgeting.Application;

public static class BudgetingEndpoints
{
    public static IEndpointRouteBuilder MapBudgetingEndpoints(this IEndpointRouteBuilder builder)
    {
        // Budget cells moved to the Core module (owned by the cost center); see /cost-centers/{id}/unit-budgets.
        var requisitions = builder.MapGroup("/requisitions");
        requisitions.MapEndpoint<ListRequisitions>();
        requisitions.MapEndpoint<ListMyDecisions>(); // literal segment wins over /{id} in routing
        requisitions.MapEndpoint<ListMyTriageDecisions>();
        requisitions.MapEndpoint<GetRequisitionSlaReport>(); // literal /sla-report before /{id}
        requisitions.MapEndpoint<GetRequisitionStageCounts>(); // literal /stage-counts before /{id}
        requisitions.MapEndpoint<GetQueueBudgetProjection>(); // literal /budget-projection before /{id}
        requisitions.MapEndpoint<GetRequisition>();
        requisitions.MapEndpoint<CreateRequisition>();
        requisitions.MapEndpoint<UpdateRequisition>();
        requisitions.MapEndpoint<DeleteRequisition>();
        requisitions.MapEndpoint<UploadRequisitionAttachment>();
        requisitions.MapEndpoint<RegisterRequisitionAttachment>(); // by-key (boleto reader reuses the parse blob)
        requisitions.MapEndpoint<DownloadRequisitionAttachment>();
        requisitions.MapEndpoint<UpdateRequisitionAttachmentKind>(); // PATCH — reclassify (write or triage)
        requisitions.MapEndpoint<DeleteRequisitionAttachment>();
        // Ações da esteira de aprovação (AEL-789; esteira remapeada em AEL-841).
        requisitions.MapEndpoint<SubmitRequisition>();
        requisitions.MapEndpoint<ApproveTriageRequisition>();
        requisitions.MapEndpoint<SubmitFinancialComplementRequisition>();
        // Boleto reader via PDF/AI (AEL-938): presigned upload + stateless parse (literal routes).
        requisitions.MapEndpoint<RequestBoletoUploadUrl>();
        requisitions.MapEndpoint<ParseBoleto>();
        requisitions.MapEndpoint<DecodePixPayloadEmv>(); // PIX copia e cola decode (AEL-963), literal /pix/... route

        requisitions.MapEndpoint<ReturnRequisitionForComplement>();
        requisitions.MapEndpoint<ReturnRequisitionToFinancialComplement>();
        requisitions.MapEndpoint<ResubmitRequisition>();
        requisitions.MapEndpoint<ApproveRequisition>();
        // AEL-877 — recurring requisitions: launch each month's invoice / terminate the recurrence.
        requisitions.MapEndpoint<AddRecurrenceCycle>();
        requisitions.MapEndpoint<TerminateRecurrenceRequisition>();
        requisitions.MapEndpoint<CancelRequisition>();
        // Painel de controle orçamentário (server-side, por competência) — substitui a soma no cliente.
        requisitions.MapEndpoint<GetDimensionBudgetControl>();
        requisitions.MapEndpoint<GetRequisitionBudgetControl>();
        requisitions.MapEndpoint<GetRequisitionBudgetControls>(); // batch by ids (queue) — literal /budget-controls before /{id}
        requisitions.MapEndpoint<GetCostCenterBudgetSummary>();
        requisitions.MapEndpoint<GetCostCenterRealized>();

        var projects = builder.MapGroup("/projects");
        projects.MapEndpoint<ListProjects>();
        projects.MapEndpoint<GetProjectMetricsList>(); // literal /metrics before /{id}
        projects.MapEndpoint<GetProject>();
        projects.MapEndpoint<GetProjectMetrics>();
        projects.MapEndpoint<CreateProject>();
        projects.MapEndpoint<UpdateProject>();
        projects.MapEndpoint<DeleteProject>();

        // Budget adjustment (AEL-791): ceiling change (supplement/reduction/transfer) under a
        // triage -> approval workflow (with an approval limit) that mutates the Budget on approval.
        var budgetAdjustments = builder.MapGroup("/budget-adjustments");
        budgetAdjustments.MapEndpoint<ListBudgetAdjustments>();
        budgetAdjustments.MapEndpoint<GetBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<CreateBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<UpdateBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<DeleteBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<SendToApprovalBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<ApproveBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<CancelBudgetAdjustment>();
        budgetAdjustments.MapEndpoint<ReverseBudgetAdjustment>();
        return builder;
    }
}
