using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Budgeting.Application;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Application.Sla;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Budgeting.Application.Requisitions;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Budgeting.Infrastructure.Dimensions;
using Ulbra.One.Budgeting.Infrastructure.FiscalDocuments;
using Ulbra.One.Budgeting.Infrastructure.Realtime;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Budgeting.Infrastructure;

public static class BudgetingModule
{
    public static IServiceCollection AddBudgetingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RequisitionSlaOptions>(configuration.GetSection(RequisitionSlaOptions.SectionName));
        // Active SLA escalation (AEL-872): the periodic sweep that detects overdue stages and publishes
        // RequisitionSlaBreached. Driven by the ACA cron Job (--job requisition-sla-sweep); see JobRunner.
        services.AddScoped<IRequisitionSlaSweepService, RequisitionSlaSweepService>();
        services.AddScoped<IBudgetDimensionLookup, BudgetDimensionLookup>();
        services.AddScoped<IVendorLookup, VendorLookup>();
        services.AddScoped<ICostCenterBudgetMutator, CostCenterBudgetMutator>();
        services.AddScoped<ICostCenterBudgetLookup, CostCenterBudgetLookup>();
        services.AddScoped<IBudgetControl, BudgetControlService>();
        // Boleto PDF/image reader (AEL-938): injects IAiCompletion directly, decoupled from Finance's fiscal extractor.
        services.AddScoped<IBoletoExtractor, BoletoAiExtractor>();
        services.AddScoped<IPresenceAuthorizer, RequisitionPresenceAuthorizer>();

        // Duplicate-boleto seam: requisition plans and Conta Fixa cycles hold linhas digitáveis that Finance
        // cannot see, so Budgeting contributes them to the shared check.
        services.AddScoped<IChargeReferenceSource, BudgetingChargeReferenceSource>();

        services.AddModelConfiguration(typeof(BudgetingModule).Assembly);

        return services;
    }
}
