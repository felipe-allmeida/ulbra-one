using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionAttachmentConfiguration : BaseEntityConfiguration<RequisitionAttachment>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisition_attachment";

    protected override void ConfigureEntity(EntityTypeBuilder<RequisitionAttachment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired().HasDefaultValue(RequisitionAttachmentKind.Other);
        e.HasIndex(x => x.RequisitionId);
    }
}
