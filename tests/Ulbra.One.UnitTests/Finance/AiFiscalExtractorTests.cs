using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;
namespace Ulbra.One.UnitTests.Finance;

public class AiFiscalExtractorTests
{
    private sealed class StubAi(Func<object?> make) : IAiCompletion
    {
        public Task<Result<T>> CompleteAsync<T>(AiPrompt p, AiOptions? o = null, CancellationToken ct = default) where T : class
        {
            var v = make();
            return Task.FromResult(v is null
                ? Result.Failure<T>(Error.Failure(AiErrors.Disabled, "off"))
                : Result.Success((T)v));
        }
    }

    [Fact]
    public async Task Maps_ai_result_to_parsed_document()
    {
        var res = new FiscalExtractionResult("NF-9", "2026-06-01", 1500m, "12345678000199", "Acme",
            "Cloud", [new FiscalExtractionInstallment("2026-07-01", 1500m)], ["5102"], ["49019900"], 80m, 0.9);
        var sut = new AiFiscalExtractor(new StubAi(() => res));

        var parsed = await sut.ExtractAsync("nota.pdf", new byte[] { 1 }, default);

        Assert.Equal(FiscalDocumentFormat.Pdf, parsed.Format);
        Assert.Equal("NF-9", parsed.DocumentNumber);
        Assert.Equal("12345678000199", parsed.IssuerDocument); // normalized digits
        Assert.Single(parsed.Installments);
        Assert.Equal(["5102"], parsed.Fiscal!.Cfops);
    }

    [Fact]
    public async Task Ai_off_returns_empty_document_not_throw()
    {
        var sut = new AiFiscalExtractor(new StubAi(() => null));
        var parsed = await sut.ExtractAsync("nota.pdf", new byte[] { 1 }, default);
        Assert.Equal(FiscalDocumentFormat.Pdf, parsed.Format);
        Assert.Null(parsed.DocumentNumber);
    }

    [Theory]
    [InlineData("Service", PayableType.Service)]
    [InlineData("product", PayableType.Product)] // case-insensitive
    [InlineData("bogus", null)]                  // hallucinated label → dropped
    [InlineData(null, null)]                     // model unsure
    public async Task Validates_and_maps_ai_type(string? aiType, PayableType? expected)
    {
        var res = new FiscalExtractionResult("NF-9", "2026-06-01", 1500m, "12345678000199", "Acme",
            "Cloud", [], null, null, null, 0.9, Type: aiType);
        var sut = new AiFiscalExtractor(new StubAi(() => res));

        var parsed = await sut.ExtractAsync("nota.pdf", new byte[] { 1 }, default);

        Assert.Equal(expected, parsed.SuggestedType);
    }
}
