using System;
using System.Threading;
using System.Threading.Tasks;
using Ulbra.One.Budgeting.Application.FiscalDocuments;
using Ulbra.One.Budgeting.Infrastructure.FiscalDocuments;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Errors;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

public class BoletoAiExtractorTests
{
    // Stub IAiCompletion: a non-null factory value maps to Result.Success; null maps to Result.Failure (AI off).
    private sealed class StubAi(Func<object?> make) : IAiCompletion
    {
        public Task<Result<T>> CompleteAsync<T>(AiPrompt p, AiOptions? o = null, CancellationToken ct = default)
            where T : class =>
            Task.FromResult(make() is { } v
                ? Result.Success((T)v)
                : Result.Failure<T>(Error.Failure(ErrorCodes.Ai.Disabled, "off")));
    }

    private static BoletoAiExtractor Sut(object? make) => new(new StubAi(() => make));

    [Fact]
    public async Task Maps_a_real_bancario_line()
    {
        var res = new BoletoExtractionResult("34195000080123320318964221470004584410000002000", "2026-09-10", null, 20m, 0.95);

        var parsed = await Sut(res).ExtractAsync("boleto.pdf", [1], default);

        Assert.Equal("34195000080123320318964221470004584410000002000", parsed.DigitableLine);
        Assert.False(parsed.IsConvenio);
        Assert.True(parsed.LengthValid);
        Assert.True(parsed.CheckDigitsOk);
        Assert.Equal(new DateOnly(2026, 9, 10), parsed.DueDate);
        Assert.Equal(20m, parsed.Amount);
    }

    [Fact]
    public async Task Normalizes_a_masked_line_to_digits()
    {
        var res = new BoletoExtractionResult("34195 00008 01233 20318 96422 14700 04584 41000 00020 00", null, null, null, 0.9);

        var parsed = await Sut(res).ExtractAsync("boleto.png", [1], default);

        Assert.Equal("34195000080123320318964221470004584410000002000", parsed.DigitableLine);
        Assert.True(parsed.LengthValid);
    }

    [Fact]
    public async Task Detects_convenio_and_validates_mod11_dv()
    {
        var res = new BoletoExtractionResult("858200000007572503282030560708202107539591904460", "2026-08-20", null, 573.03m, 0.8);

        var parsed = await Sut(res).ExtractAsync("boleto.pdf", [1], default);

        Assert.True(parsed.IsConvenio);
        Assert.True(parsed.LengthValid);
        Assert.True(parsed.CheckDigitsOk);
    }

    [Fact]
    public async Task Flags_a_bad_check_digit_as_advisory_without_failing_length()
    {
        // Real bancário line with the last digit flipped (0 → 1): still 47 digits, but the general DV no longer checks.
        var res = new BoletoExtractionResult("34195000080123320318964221470004584410000002001", null, null, null, 0.7);

        var parsed = await Sut(res).ExtractAsync("boleto.pdf", [1], default);

        Assert.True(parsed.LengthValid);      // length rule still passes (blocking rule)
        Assert.False(parsed.CheckDigitsOk);   // DV advisory catches the misread
    }

    [Fact]
    public async Task Ai_off_returns_empty_and_does_not_throw()
    {
        var parsed = await Sut(null).ExtractAsync("boleto.pdf", [1], default);

        Assert.Null(parsed.DigitableLine);
        Assert.False(parsed.LengthValid);
        Assert.Equal(0, parsed.Confidence);
    }

    [Fact]
    public async Task No_line_read_applies_nothing_even_when_a_date_came_back()
    {
        var res = new BoletoExtractionResult(null, "2026-09-10", null, 10m, 0.4);

        var parsed = await Sut(res).ExtractAsync("boleto.pdf", [1], default);

        Assert.Null(parsed.DigitableLine);
        Assert.Null(parsed.DueDate);
        Assert.Equal(0.4, parsed.Confidence);
    }
}
