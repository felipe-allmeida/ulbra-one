using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class FiscalDocumentConfiguration : BaseEntityConfiguration<FiscalDocument>
{
    protected override string Schema => Schemas.Finance;

    protected override void ConfigureEntity(EntityTypeBuilder<FiscalDocument> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.AccessKey).HasMaxLength(44);
        e.Property(x => x.DocumentNumber).HasMaxLength(60);
        e.Property(x => x.TotalAmount).HasPrecision(18, 2);
        e.Property(x => x.IssuerDocument).HasMaxLength(14);
        e.Property(x => x.IssuerName).HasMaxLength(200);
        e.Property(x => x.RecipientDocument).HasMaxLength(14);
        e.Property(x => x.RawExtractionJson).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
        // One fiscal document of record per payable (re-upload replaces it).
        e.HasIndex(x => x.PayableId).IsUnique();
    }
}
