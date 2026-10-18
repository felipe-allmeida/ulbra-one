using Ulbra.One.FirestoreImport.Mapping;
using Xunit;

namespace Ulbra.One.FirestoreImport.Tests;

public class ProjectMappingTests
{
    [Theory]
    [InlineData("Ativo", "Active")]
    [InlineData("Finalizado", "Finished")]
    [InlineData("Cancelado", "Cancelled")]
    [InlineData("", "Active")]
    [InlineData(null, "Active")]
    [InlineData("qualquer", "Active")]
    public void MapStatus_maps_legacy_labels(string? legacy, string expected)
        => Assert.Equal(expected, ProjectMapping.MapStatus(legacy));
}
