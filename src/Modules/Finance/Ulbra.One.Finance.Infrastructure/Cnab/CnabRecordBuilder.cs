using System.Globalization;
using System.Text;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// 1-based positional writer for a single fixed-width CNAB record (typically 240 bytes).
/// Each field overwrites a [start, start+length) slice; positions start filled with spaces.
/// The builder tracks which positions have been written so <see cref="Build"/> can assert
/// the whole record is covered exactly once (catching layout gaps/overlaps at author time).
/// Output is Latin-1-safe: alpha is uppercased and diacritics are stripped to ASCII.
/// </summary>
public sealed class CnabRecordBuilder
{
    private readonly char[] _buffer;
    private readonly bool[] _written;

    public CnabRecordBuilder(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Record length must be positive.");

        _buffer = new char[length];
        _written = new bool[length];
        Array.Fill(_buffer, ' ');
    }

    public int Length => _buffer.Length;

    /// <summary>Alpha field: uppercased, accent-stripped, left-justified, space-padded. Throws on overflow.</summary>
    public CnabRecordBuilder Alpha(int start, int length, string? value)
        => Alpha(start, length, value, allowTruncate: false);

    /// <summary>
    /// Alpha field. When <paramref name="allowTruncate"/> is true an over-long value is right-trimmed
    /// (e.g. favorecido names that exceed the field); otherwise overflow throws.
    /// </summary>
    public CnabRecordBuilder Alpha(int start, int length, string? value, bool allowTruncate)
    {
        var text = Sanitize(value);

        if (text.Length > length)
        {
            if (!allowTruncate)
                throw new InvalidOperationException(
                    $"Alpha value of length {text.Length} does not fit field at {start} (length {length}): '{text}'.");
            text = text[..length];
        }

        var field = new char[length];
        Array.Fill(field, ' ');
        text.CopyTo(0, field, 0, text.Length); // left-justify
        return Write(start, field);
    }

    /// <summary>Strict alpha: identical to <see cref="Alpha(int,int,string)"/> but renamed to make the
    /// no-truncation contract explicit at the call site (used for the round-trip "Seu Número" reference).</summary>
    public CnabRecordBuilder AlphaStrict(int start, int length, string? value)
        => Alpha(start, length, value, allowTruncate: false);

    /// <summary>Numeric field from an integer: right-justified, zero-padded. Throws on overflow.</summary>
    public CnabRecordBuilder Numeric(int start, int length, long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Numeric value must be non-negative.");
        return Numeric(start, length, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Numeric field from a (possibly formatted) string: keeps only digits, right-justifies, zero-pads.</summary>
    public CnabRecordBuilder Numeric(int start, int length, string? value)
    {
        var digits = DigitsOnly(value);

        if (digits.Length > length)
            throw new InvalidOperationException(
                $"Numeric value '{digits}' (length {digits.Length}) does not fit field at {start} (length {length}).");

        var field = new char[length];
        Array.Fill(field, '0');
        digits.CopyTo(0, field, length - digits.Length, digits.Length); // right-justify
        return Write(start, field);
    }

    /// <summary>Money field: 2 implied decimals, no separator (R$ 1234.56 → "...123456"). Right-justified, zero-padded.</summary>
    public CnabRecordBuilder Money(int start, int length, decimal value)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Money value must be non-negative.");

        var cents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
        return Numeric(start, length, cents);
    }

    /// <summary>Date field formatted DDMMYYYY.</summary>
    public CnabRecordBuilder Date(int start, int length, DateOnly value)
    {
        if (length != 8)
            throw new ArgumentException("CNAB date fields are DDMMYYYY (length 8).", nameof(length));
        return Numeric(start, length, value.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
    }

    /// <summary>Explicitly mark a blank (space-filled) field — documents intent and reserves the positions.</summary>
    public CnabRecordBuilder Blank(int start, int length)
    {
        var field = new char[length];
        Array.Fill(field, ' ');
        return Write(start, field);
    }

    /// <summary>Renders the record. The buffer is always exactly the declared length (unwritten positions stay spaces).</summary>
    public string Build() => new(_buffer);

    /// <summary>
    /// Like <see cref="Build"/> but first asserts every position was written exactly once — catching
    /// layout gaps/overlaps at author time. The writer uses this for each real CNAB record.
    /// </summary>
    public string BuildComplete()
    {
        for (var i = 0; i < _written.Length; i++)
        {
            if (!_written[i])
                throw new InvalidOperationException(
                    $"Record position {i + 1} (1-based) was never written — the field map does not cover the full {_buffer.Length}-byte record.");
        }

        return new string(_buffer);
    }

    private CnabRecordBuilder Write(int start, char[] field)
    {
        if (start < 1)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Positions are 1-based.");

        var endExclusive = start - 1 + field.Length;
        if (endExclusive > _buffer.Length)
            throw new InvalidOperationException(
                $"Field at {start} (length {field.Length}) overruns the {_buffer.Length}-byte record.");

        for (var i = 0; i < field.Length; i++)
        {
            var index = start - 1 + i;
            if (_written[index])
                throw new InvalidOperationException(
                    $"Field at {start} (length {field.Length}) overlaps an already-written position {index + 1}.");
            _buffer[index] = field[i];
            _written[index] = true;
        }

        return this;
    }

    /// <summary>Uppercase + strip diacritics so the result is Latin-1 / ASCII-safe.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // drop combining accents
            sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string DigitsOnly(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsAsciiDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
