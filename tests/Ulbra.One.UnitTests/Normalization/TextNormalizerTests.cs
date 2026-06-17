using Ulbra.One.Common.Application.Normalization;

namespace Ulbra.One.UnitTests.Normalization;

public class TextNormalizerTests
{
    private sealed record Req
    {
        [NormalizeText] public string Name { get; set; } = string.Empty;
        [NormalizeText] public string? Optional { get; set; }
        public string Untouched { get; set; } = string.Empty;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("abc", "abc")]
    [InlineData("  abc  ", "abc")]
    [InlineData("a  b", "a b")]
    [InlineData("  ACME   SA  ", "ACME SA")]
    [InlineData("a\t\tb", "a b")]
    [InlineData("a \n b", "a b")]
    public void Collapse_TrimsAndCollapses(string? input, string? expected)
    {
        Assert.Equal(expected, TextNormalizer.Collapse(input));
    }

    [Fact]
    public void Collapse_ReturnsSameReferenceWhenAlreadyClean()
    {
        const string clean = "already clean";
        Assert.Same(clean, TextNormalizer.Collapse(clean));
    }

    [Fact]
    public void Normalize_OnlyTouchesAnnotatedStringProperties()
    {
        var req = new Req { Name = "  ACME   SA  ", Optional = null, Untouched = "  keep  me  " };

        TextNormalizer.Normalize(req);

        Assert.Equal("ACME SA", req.Name);
        Assert.Null(req.Optional);
        Assert.Equal("  keep  me  ", req.Untouched);
    }

    [Fact]
    public void Normalize_CollapsesAnnotatedNullableWhenSet()
    {
        var req = new Req { Name = "ok", Optional = "  x   y  " };

        TextNormalizer.Normalize(req);

        Assert.Equal("x y", req.Optional);
    }

    [Fact]
    public void Normalize_NullTarget_DoesNotThrow()
    {
        TextNormalizer.Normalize(null);
    }
}
