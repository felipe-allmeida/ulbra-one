using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

namespace Ulbra.One.UnitTests.Finance;

public class AiClassificationResolverTests
{
    private static readonly Guid AccId = Guid.NewGuid();

    private static readonly IReadOnlyList<AiChartAccountRef> Catalog = new[]
    {
        new AiChartAccountRef(AccId, "3.1.1"),
    };

    [Fact]
    public void Resolves_matching_code_to_its_id()
    {
        var s = new PayableClassificationSuggestion("3.1.1", 0.9, "ok");

        var acc = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Equal(AccId, acc);
    }

    [Fact]
    public void Hallucinated_code_resolves_to_null()
    {
        var s = new PayableClassificationSuggestion("9.9.9", 0.9, "ok");

        var acc = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Null(acc);
    }

    [Fact]
    public void Null_code_resolves_to_null()
    {
        var s = new PayableClassificationSuggestion(null, 0.9, "ok");

        var acc = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Null(acc);
    }
}
