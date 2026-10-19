using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionConfiguration : BaseEntityConfiguration<Requisition>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisitions";

    protected override void ConfigureEntity(EntityTypeBuilder<Requisition> e)
    {
        e.HasKey(x => x.Id);

        e.Property(x => x.Description).HasMaxLength(500).IsRequired();
        e.Property(x => x.Observation).HasMaxLength(1000);
        e.Property(x => x.EstimateValue).HasPrecision(18, 2);
        e.Property(x => x.TriageApprovedValue).HasPrecision(18, 2);
        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.EstimateType).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.Property(x => x.PaymentFrequency).HasConversion<string>().HasMaxLength(16);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.StartCompetency).HasMaxLength(7).IsRequired();
        e.Property(x => x.EndCompetency).HasMaxLength(7);

        // Per-company friendly sequential code; unique so the endpoint's max+1 assignment races safely.
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();

        // Optional project link (AEL-792) — loose Guid like the other dimensions; indexed because the
        // project tracking screen filters requisitions by it.
        e.HasIndex(x => x.ProjectId);

        // Vendor (AEL-793) — loose Guid into the Registry, indexed for vendor-scoped queries/reports.
        e.HasIndex(x => x.VendorId);

        // Serves the SLA escalation sweep's coarse prefilter (active stage + entry older than threshold) — AEL-872.
        e.HasIndex(x => new { x.Status, x.StatusChangedAt });

        // Serves the per-status stage counts (GROUP BY status filtered by company) and the browse list, which
        // almost always filters CompanyId + Status. Without it a company's count is a scan.
        e.HasIndex(x => new { x.CompanyId, x.Status });

        // The requester dashboard counts only "mine" (CreatedBy) within a company — this backs that path.
        e.HasIndex(x => new { x.CompanyId, x.CreatedBy });

        // Conta Fixa is unique per (fornecedor, unidade, natureza=cost center) while active (AEL-881): no two
        // "electricity bills" for the same unit. Partial unique index — active = not Cancelled and not Finalized
        // (a superseded/ended one frees the key). Backstops the CreateRequisition pre-check against the race.
        // Type/Status are stored as strings (HasConversion<string>), so the filter uses string literals.
        e.HasIndex(x => new { x.VendorId, x.UnitId, x.CostCenterId })
            .IsUnique()
            .HasFilter("type = 'FixedExpense' AND status NOT IN ('Cancelled', 'Finalized')")
            .HasDatabaseName("ix_requisitions_fixed_expense_unique");

        // Cross-schema references (company/unit/cost center) are loose Guids — no HasOne/HasForeignKey.
        // Attachments and transition history are owned collections within the aggregate (real intra-schema
        // FK, cascade). The transition's ActorId is a loose Guid into identity.user — no cross-schema FK.
        e.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(a => a.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        e.HasMany(x => x.Transitions)
            .WithOne()
            .HasForeignKey(t => t.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Real payments fed back from Finance (AEL-793) — owned collection within the aggregate.
        e.HasMany(x => x.Payments)
            .WithOne()
            .HasForeignKey(p => p.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Authored payment plan (financial complement, AEL-844) — owned collection within the aggregate.
        e.HasMany(x => x.Installments)
            .WithOne()
            .HasForeignKey(i => i.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Recurrence cycles launched manually after approval (AEL-877) — owned collection within the aggregate.
        e.HasMany(x => x.Cycles)
            .WithOne()
            .HasForeignKey(c => c.RequisitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
