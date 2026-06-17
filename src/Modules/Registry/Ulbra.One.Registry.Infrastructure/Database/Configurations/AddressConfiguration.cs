using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Registry.Infrastructure.Database.Configurations;

internal static class AddressConfiguration
{
    internal static void Configure<T>(OwnedNavigationBuilder<T, Address> a)
        where T : class
    {
        a.Property(x => x.Cep).HasMaxLength(8);
        a.Property(x => x.Street).HasMaxLength(200);
        a.Property(x => x.Number).HasMaxLength(20);
        a.Property(x => x.Complement).HasMaxLength(100);
        a.Property(x => x.Neighborhood).HasMaxLength(100);
        a.Property(x => x.City).HasMaxLength(100);
        a.Property(x => x.State).HasMaxLength(2);
    }
}
