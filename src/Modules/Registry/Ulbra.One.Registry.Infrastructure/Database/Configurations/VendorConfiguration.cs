using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Infrastructure.Database.Configurations;

public sealed class VendorConfiguration : BaseEntityConfiguration<VendorEntity>
{
    protected override string Schema => Schemas.Registry;

    protected override void ConfigureEntity(EntityTypeBuilder<VendorEntity> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.TradeName).HasMaxLength(200);
        e.Property(x => x.Document).HasMaxLength(14).IsRequired();
        e.Property(x => x.Email).HasMaxLength(200);
        e.Property(x => x.Phone).HasMaxLength(20);
        e.Property(x => x.PaymentTerms).HasMaxLength(100);
        e.Property(x => x.TaxRegime).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.SimplesAnexo).HasConversion<string>().HasMaxLength(4);
        e.OwnsOne(x => x.Address, AddressConfiguration.Configure);
        e.OwnsOne(x => x.BankInfo, BankInfoConfiguration.Configure);
        e.OwnsOne(x => x.PaymentSchedule, ps =>
        {
            ps.Property(p => p.Mode).HasConversion<string>().HasMaxLength(20);
            ps.Property(p => p.DayOfMonth);
        });
        // Accepted payment methods — a list of PaymentMethod persisted as jsonb (enum names).
        // Precedent: FiscalDocument.Cfops/Ncms. Default '[]' backfills existing vendors.
        var acceptedMethodsToJson = new ValueConverter<IReadOnlyList<PaymentMethod>, string>(
            v => JsonSerializer.Serialize(v.Select(m => m.ToString()), (JsonSerializerOptions?)null),
            v => (JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Select(Enum.Parse<PaymentMethod>).ToList());
        var acceptedMethodsComparer = new ValueComparer<IReadOnlyList<PaymentMethod>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, m) => HashCode.Combine(h, (int)m)),
            v => v.ToList());
        e.Property(x => x.AcceptedPaymentMethods).HasConversion(acceptedMethodsToJson)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        e.Property(x => x.AcceptedPaymentMethods).Metadata.SetValueComparer(acceptedMethodsComparer);
        // Per-tax withholding rate overrides — a list of {TaxType, Rate} persisted as jsonb. Default '[]' backfills.
        var withholdingRatesToJson = new ValueConverter<IReadOnlyList<VendorWithholdingRate>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<VendorWithholdingRate>>(v, (JsonSerializerOptions?)null)
                 ?? new List<VendorWithholdingRate>());
        var withholdingRatesComparer = new ValueComparer<IReadOnlyList<VendorWithholdingRate>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, r) => HashCode.Combine(h, r.GetHashCode())),
            v => v.ToList());
        e.Property(x => x.WithholdingRates).HasConversion(withholdingRatesToJson)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        e.Property(x => x.WithholdingRates).Metadata.SetValueComparer(withholdingRatesComparer);
        // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
        // so the same document can be re-registered after a soft-delete.
        e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
