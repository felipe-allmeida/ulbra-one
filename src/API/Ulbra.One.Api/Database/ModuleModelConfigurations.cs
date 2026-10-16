using System.Reflection;
using Ulbra.One.Accounting.Infrastructure;
using Ulbra.One.Budgeting.Infrastructure;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Infrastructure;
using Ulbra.One.Finance.Infrastructure;
using Ulbra.One.Fiscal.Infrastructure;
using Ulbra.One.Identity.Infrastructure;
using Ulbra.One.Notifications.Infrastructure;
using Ulbra.One.Registry.Infrastructure;
using AuditInfrastructure = Ulbra.One.Audit.Infrastructure;

namespace Ulbra.One.Api.Database;

/// <summary>
/// The design-time view of which modules contribute to the model. At runtime each AddXModule
/// registers its own IModuleModelConfiguration; the EF tooling has no DI container, so it
/// rebuilds the same set from here. ModelRegistrationTests fails if the two drift apart.
/// </summary>
public static class ModuleModelConfigurations
{
    /// <summary>Modules whose contribution is a plain assembly scan.</summary>
    public static readonly Assembly[] All =
    [
        typeof(CoreModule).Assembly,
        typeof(FinanceModule).Assembly,
        typeof(IdentityModule).Assembly,
        typeof(RegistryModule).Assembly,
        typeof(AccountingModule).Assembly,
        typeof(FiscalModule).Assembly,
        typeof(BudgetingModule).Assembly,
        typeof(NotificationsModule).Assembly,
    ];

    /// <summary>Every contribution, including modules with custom (non-scan) mapping.</summary>
    public static IEnumerable<IModuleModelConfiguration> Create() =>
    [
        .. All.Select(a => new AssemblyModelConfiguration(a)),
        new AuditInfrastructure.AuditLogModelConfiguration(),
    ];
}
