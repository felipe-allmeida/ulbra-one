using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PayableAttachmentConfiguration : IEntityTypeConfiguration<PayableAttachment>
{
    public void Configure(EntityTypeBuilder<PayableAttachment> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
        e.HasIndex(x => x.PayableId);
    }
}
