using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Infrastructure.Database.Configurations;

/// <summary>Shared helper for configuring the owned BankInfo value object — mirrors AddressConfiguration. Invoked by VendorConfiguration.</summary>
internal static class BankInfoConfiguration
{
    internal static void Configure<T>(OwnedNavigationBuilder<T, BankInfo> b)
        where T : class
    {
        b.Property(x => x.BankCode).HasMaxLength(10);
        b.Property(x => x.Branch).HasMaxLength(10);
        b.Property(x => x.Account).HasMaxLength(20);
        b.Property(x => x.AccountDigit).HasMaxLength(2);
        b.Property(x => x.PixKey).HasMaxLength(100);
    }
}
