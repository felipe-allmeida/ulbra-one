using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PayableAttachmentConfiguration : BaseEntityConfiguration<PayableAttachment>
{
    protected override string Schema => "finance";

    protected override void ConfigureEntity(EntityTypeBuilder<PayableAttachment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
        e.HasIndex(x => x.PayableId);
    }
}
