using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Audit.Infrastructure;

/// <summary>
/// Audit's contribution to AppDbContext: the audit_log entity, mapped read/write but excluded
/// from AppDbContext migrations — AuditContext owns the table.
/// </summary>
public sealed class AuditLogModelConfiguration : IModuleModelConfiguration
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.MapAuditLog(ownsTable: false);
}
