using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

        // Fiscal tax signals (NF-e only).
        e.Property(x => x.IcmsAmount).HasPrecision(18, 2);
        e.Property(x => x.IpiAmount).HasPrecision(18, 2);
        e.Property(x => x.PisAmount).HasPrecision(18, 2);
        e.Property(x => x.CofinsAmount).HasPrecision(18, 2);
        e.Property(x => x.TotalTaxesAmount).HasPrecision(18, 2);

        var strListToJson = new ValueConverter<IReadOnlyList<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<IReadOnlyList<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
        var strListComparer = new ValueComparer<IReadOnlyList<string>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());
        e.Property(x => x.Cfops).HasConversion(strListToJson).Metadata.SetValueComparer(strListComparer);
        e.Property(x => x.Cfops).HasColumnType("jsonb");
        e.Property(x => x.Ncms).HasConversion(strListToJson).Metadata.SetValueComparer(strListComparer);
        e.Property(x => x.Ncms).HasColumnType("jsonb");

        // Fiscal signals (NFS-e only).
        e.Property(x => x.Lc116Subitem).HasMaxLength(10);
        e.Property(x => x.MunicipalServiceCode).HasMaxLength(20);
        e.Property(x => x.ServiceMunicipalityIbge).HasMaxLength(7);
        e.Property(x => x.Cnae).HasMaxLength(7);
        e.Property(x => x.NotaIssRate).HasPrecision(5, 4);
    }
}
