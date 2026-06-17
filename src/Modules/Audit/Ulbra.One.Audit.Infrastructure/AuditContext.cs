using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Domain.Entities;

namespace Ulbra.One.Audit.Infrastructure;

public sealed class AuditContext(DbContextOptions<AuditContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.MapAuditLog(ownsTable: true);
}
