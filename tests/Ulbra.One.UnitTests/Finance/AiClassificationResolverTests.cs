using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class AiClassificationResolverTests
{
    private static readonly Guid AccId = Guid.NewGuid();
    private static readonly Guid FinId = Guid.NewGuid();

    private static readonly IReadOnlyList<AiChartAccountRef> Catalog = new[]
    {
        new AiChartAccountRef(AccId, "3.1.1", ChartType.Accounting),
        new AiChartAccountRef(FinId, "2.1.1", ChartType.Financial),
    };

    [Fact]
    public void Resolves_matching_codes_to_their_ids()
    {
        var s = new PayableClassificationSuggestion("3.1.1", "2.1.1", 0.9, "ok");

        var (acc, fin) = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Equal(AccId, acc);
        Assert.Equal(FinId, fin);
    }

    [Fact]
    public void Hallucinated_code_resolves_to_null()
    {
        var s = new PayableClassificationSuggestion("9.9.9", null, 0.9, "ok");

        var (acc, fin) = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Null(acc);
        Assert.Null(fin);
    }

    [Fact]
    public void Code_of_wrong_axis_resolves_to_null()
    {
        // "2.1.1" is Financial; offering it as the accounting code must not match.
        var s = new PayableClassificationSuggestion("2.1.1", null, 0.9, "ok");

        var (acc, _) = AiClassificationResolver.Resolve(s, Catalog);

        Assert.Null(acc);
    }
}
