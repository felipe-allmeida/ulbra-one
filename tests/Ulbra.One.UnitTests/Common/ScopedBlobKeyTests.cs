using System;
using Ulbra.One.Common.Application.Storage;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

public class ScopedBlobKeyTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Root = "requisition-boleto";

    [Fact]
    public void PrefixFor_namespaces_by_root_and_company()
        => Assert.Equal("requisition-boleto/11111111111111111111111111111111/", ScopedBlobKey.PrefixFor(Root, Company));

    [Fact]
    public void BelongsTo_true_for_key_in_the_company_prefix()
        => Assert.True(ScopedBlobKey.BelongsTo(ScopedBlobKey.PrefixFor(Root, Company) + "boleto.pdf", Root, Company));

    [Fact]
    public void BelongsTo_false_for_another_company()
        => Assert.False(ScopedBlobKey.BelongsTo(ScopedBlobKey.PrefixFor(Root, Other) + "boleto.pdf", Root, Company));

    [Fact]
    public void BelongsTo_false_for_a_different_root()
        => Assert.False(ScopedBlobKey.BelongsTo("fiscal/11111111111111111111111111111111/x.pdf", Root, Company));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("requisition-boleto/11111111111111111111111111111111/../../etc/passwd")] // traversal
    [InlineData("/requisition-boleto/11111111111111111111111111111111/x.pdf")] // absolute
    [InlineData("requisition-boleto\\11111111111111111111111111111111\\x.pdf")] // backslash
    public void BelongsTo_false_for_malformed_or_traversal(string? key)
        => Assert.False(ScopedBlobKey.BelongsTo(key, Root, Company));

    [Fact]
    public void IsWellFormed_rejects_control_chars()
        => Assert.False(ScopedBlobKey.IsWellFormed("requisition-boleto/x\ty.pdf"));
}
