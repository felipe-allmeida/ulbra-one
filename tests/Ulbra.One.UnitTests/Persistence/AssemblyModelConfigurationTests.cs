using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.UnitTests.Persistence;

public class AssemblyModelConfigurationTests
{
    // A throwaway entity + config living in this test assembly, so the test can prove the
    // helper picks up IEntityTypeConfiguration implementations by assembly scan.
    private class Widget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class WidgetConfiguration : IEntityTypeConfiguration<Widget>
    {
        public void Configure(EntityTypeBuilder<Widget> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(42);
        }
    }

    [Fact]
    public void Apply_RegistersEntityConfigurationsFromTheAssembly()
    {
        var modelBuilder = new ModelBuilder();

        new AssemblyModelConfiguration(typeof(WidgetConfiguration).Assembly).Apply(modelBuilder);

        var widget = modelBuilder.Model.FindEntityType(typeof(Widget));
        Assert.NotNull(widget);
        Assert.Equal(42, widget!.FindProperty(nameof(Widget.Name))!.GetMaxLength());
    }
}
