using Ulbra.One.Audit.Infrastructure;
using Xunit;

namespace Ulbra.One.UnitTests.Audit;

public class AuditDiffTests
{
    private static readonly IReadOnlySet<string> Ignored =
        new HashSet<string> { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "DeletedAt" };

    [Fact]
    public void Compute_reports_only_changed_business_columns()
    {
        var original = new Dictionary<string, object?> { ["Status"] = "Approved", ["Total"] = 100m, ["UpdatedAt"] = "t0" };
        var current  = new Dictionary<string, object?> { ["Status"] = "Settled",  ["Total"] = 100m, ["UpdatedAt"] = "t1" };
        var (oldJson, newJson, affectedJson, hasChange) = AuditDiff.Compute(original, current, Ignored);
        Assert.True(hasChange);
        Assert.Contains("Status", affectedJson);
        Assert.DoesNotContain("Total", affectedJson);
        Assert.DoesNotContain("UpdatedAt", affectedJson);
        Assert.Contains("Approved", oldJson);
        Assert.Contains("Settled", newJson);
    }

    [Fact]
    public void Compute_returns_no_change_when_only_metadata_changed()
    {
        var original = new Dictionary<string, object?> { ["Status"] = "Approved", ["UpdatedAt"] = "t0" };
        var current  = new Dictionary<string, object?> { ["Status"] = "Approved", ["UpdatedAt"] = "t1" };
        var (_, _, _, hasChange) = AuditDiff.Compute(original, current, Ignored);
        Assert.False(hasChange);
    }
}
