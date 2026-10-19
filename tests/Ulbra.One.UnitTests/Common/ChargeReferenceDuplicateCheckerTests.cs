using Ulbra.One.Common.Application.Validation;
using Xunit;

namespace Ulbra.One.UnitTests.Common;

public class ChargeReferenceDuplicateCheckerTests
{
    private static readonly string LineA = new('1', 47);
    private static readonly string LineB = "8" + new string('2', 47);

    // ---------- DuplicateWithin: the same request carrying one line twice ----------

    [Fact]
    public void DuplicateWithin_finds_a_line_repeated_across_two_installments()
        => Assert.Equal(LineA, ChargeReferenceDuplicateChecker.DuplicateWithin(ChargeReferenceKind.BoletoLine, [LineA, LineB, LineA]));

    [Fact]
    public void DuplicateWithin_compares_by_digits_so_a_masked_line_matches_an_unmasked_one()
        => Assert.NotNull(ChargeReferenceDuplicateChecker.DuplicateWithin(ChargeReferenceKind.BoletoLine, [LineA, MaskOf(LineA)]));

    [Fact]
    public void DuplicateWithin_allows_distinct_lines()
        => Assert.Null(ChargeReferenceDuplicateChecker.DuplicateWithin(ChargeReferenceKind.BoletoLine, [LineA, LineB]));

    [Fact]
    public void DuplicateWithin_ignores_blank_lines_so_two_pix_installments_never_collide()
        => Assert.Null(ChargeReferenceDuplicateChecker.DuplicateWithin(ChargeReferenceKind.BoletoLine, [null, "", "   ", LineA]));

    // ---------- FindExistingAsync: the line already stored somewhere else ----------

    [Fact]
    public async Task FindExistingAsync_returns_the_usage_reported_by_a_source()
    {
        var usage = new ChargeReferenceUsage(Digits(LineA), "payable", "Título 1234");
        var checker = new ChargeReferenceDuplicateChecker([new StubSource(usage)]);

        var found = await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [LineA], excludeOwnerIds: [], CancellationToken.None);

        Assert.Equal(usage, found);
    }

    [Fact]
    public async Task FindExistingAsync_queries_every_source_so_another_module_still_blocks()
    {
        var usage = new ChargeReferenceUsage(Digits(LineA), "requisition", "Requisição 56");
        var checker = new ChargeReferenceDuplicateChecker([new StubSource(null), new StubSource(usage)]);

        var found = await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [LineA], excludeOwnerIds: [], CancellationToken.None);

        Assert.Equal(usage, found);
    }

    [Fact]
    public async Task FindExistingAsync_asks_the_sources_for_digits_only()
    {
        var source = new StubSource(null);
        var checker = new ChargeReferenceDuplicateChecker([source]);

        await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [MaskOf(LineA)], excludeOwnerIds: [], CancellationToken.None);

        Assert.Equal([Digits(LineA)], source.Asked);
    }

    [Fact]
    public async Task FindExistingAsync_returns_null_when_no_source_knows_the_line()
    {
        var checker = new ChargeReferenceDuplicateChecker([new StubSource(null)]);

        Assert.Null(await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [LineA], excludeOwnerIds: [], CancellationToken.None));
    }

    [Fact]
    public async Task FindExistingAsync_skips_the_sources_entirely_when_no_boleto_line_is_provided()
    {
        var source = new StubSource(null);
        var checker = new ChargeReferenceDuplicateChecker([source]);

        Assert.Null(await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [null, "  "], excludeOwnerIds: [], CancellationToken.None));
        Assert.False(source.WasQueried);
    }

    /// <summary>A requisition-sourced title carries the same line as the requisition that generated it, so an
    /// edit must exclude BOTH owners — otherwise the title conflicts with its own origin.</summary>
    [Fact]
    public async Task FindExistingAsync_passes_every_owner_to_exclude_so_an_edit_does_not_collide_with_its_own_origin()
    {
        Guid[] owners = [Guid.NewGuid(), Guid.NewGuid()];
        var source = new StubSource(null);
        var checker = new ChargeReferenceDuplicateChecker([source]);

        await checker.FindExistingAsync(ChargeReferenceKind.BoletoLine, [LineA], owners, CancellationToken.None);

        Assert.Equal(owners, source.ExcludedOwnerIds);
    }

    // ---------- PIX Copia e Cola (AEL-963) ----------

    private const string DynamicQr =
        "00020101021226560014br.gov.bcb.pix2534pix.example.com/qr/v2/abc123def4565204000053039865802BR5912FULANO TESTE6009SAO PAULO62070503***6304D8B5";
    [Fact]
    public void DuplicateWithin_finds_a_dynamic_qr_repeated_across_two_installments()
        => Assert.NotNull(ChargeReferenceDuplicateChecker.DuplicateWithin(
            ChargeReferenceKind.PixPayloadEmv, [DynamicQr, DynamicQr]));

    [Fact]
    public async Task FindExistingAsync_carries_the_kind_so_a_source_queries_the_right_column()
    {
        var source = new StubSource(null);
        var checker = new ChargeReferenceDuplicateChecker([source]);

        await checker.FindExistingAsync(ChargeReferenceKind.PixPayloadEmv, [DynamicQr], [], CancellationToken.None);

        Assert.Equal(ChargeReferenceKind.PixPayloadEmv, source.AskedKind);
        Assert.Equal([DynamicQr], source.Asked);
    }

    [Fact]
    public async Task A_boleto_line_and_a_pix_payload_never_collide_with_each_other()
    {
        var usage = new ChargeReferenceUsage(Digits(LineA), "payable", "Título 1234");
        var checker = new ChargeReferenceDuplicateChecker([new StubSource(usage)]);

        // The stub only knows the boleto line; asking for PIX must not match it.
        Assert.Null(await checker.FindExistingAsync(
            ChargeReferenceKind.PixPayloadEmv, [DynamicQr], [], CancellationToken.None));
    }

    private static string Digits(string line) => new(line.Where(char.IsAsciiDigit).ToArray());

    /// <summary>The line as the frontend's masked input sends it — separators the digits comparison must drop.</summary>
    private static string MaskOf(string line) => string.Join('.', line.Chunk(5).Select(c => new string(c)));

    private sealed class StubSource(ChargeReferenceUsage? usage) : IChargeReferenceSource
    {
        public IReadOnlyCollection<string> Asked { get; private set; } = [];
        public ChargeReferenceKind AskedKind { get; private set; }
        public IReadOnlyCollection<Guid> ExcludedOwnerIds { get; private set; } = [];
        public bool WasQueried { get; private set; }

        public Task<ChargeReferenceUsage?> FindAsync(
            ChargeReferenceKind kind, IReadOnlyCollection<string> references,
            IReadOnlyCollection<Guid> excludeOwnerIds, CancellationToken ct)
        {
            WasQueried = true;
            AskedKind = kind;
            Asked = references;
            ExcludedOwnerIds = excludeOwnerIds;
            return Task.FromResult(usage is not null && references.Contains(usage.Value) ? usage : null);
        }
    }
}
