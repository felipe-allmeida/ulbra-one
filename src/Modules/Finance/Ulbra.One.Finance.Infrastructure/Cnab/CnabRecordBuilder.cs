using System.Globalization;
using System.Text;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// 1-based positional writer for a single fixed-width CNAB record (typically 240 bytes).
/// Each field overwrites a [start, start+length) slice; positions start filled with spaces.
/// The builder tracks which positions have been written so <see cref="Build"/> can assert
/// the whole record is covered exactly once (catching layout gaps/overlaps at author time).
/// Output is Latin-1-safe: alpha is upper-cased (or lower-cased via <see cref="AlphaLower"/> for the
/// case-sensitive PIX key) and diacritics are stripped to ASCII.
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
        => WriteAlpha(start, length, Sanitize(value), allowTruncate);

    /// <summary>Alpha field lower-cased instead of upper-cased, for case-sensitive content — the PIX
    /// key (Seg B 128-226), where e-mail and EVP/UUID keys must not be folded to upper-case (the PIX
    /// DICT normalizes to lower-case). Still accent-stripped + Latin-1-safe.</summary>
    public CnabRecordBuilder AlphaLower(int start, int length, string? value, bool allowTruncate = false)
        => WriteAlpha(start, length, Sanitize(value, CnabCase.Lower), allowTruncate);

    /// <summary>Alpha field with the original case preserved (still accent-stripped + Latin-1-safe), for
    /// content where case is significant and must be written byte-exact — the PIX QR payload URL
    /// (Seg J52-PIX 132-210, Nota G041). Upper/lower folding could make the URL fail to resolve.</summary>
    public CnabRecordBuilder AlphaExact(int start, int length, string? value, bool allowTruncate = false)
        => WriteAlpha(start, length, Sanitize(value, CnabCase.Preserve), allowTruncate);

    private CnabRecordBuilder WriteAlpha(int start, int length, string text, bool allowTruncate)
    {
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

    /// <summary>Numeric field from a (possibly formatted) string: keeps only digits, right-justifies, zero-pads. Throws on overflow.</summary>
    public CnabRecordBuilder Numeric(int start, int length, string? value)
        => Numeric(start, length, value, allowTruncate: false);

    /// <summary>
    /// Numeric field from a (possibly formatted) string. When <paramref name="allowTruncate"/> is true an
    /// over-long value keeps its leading digits (drops the least-significant tail) instead of throwing —
    /// only sensible for informational fields that can't affect the transfer (address número/CEP), never
    /// for money, counters or account/agency identifiers.
    /// </summary>
    public CnabRecordBuilder Numeric(int start, int length, string? value, bool allowTruncate)
    {
        var digits = DigitsOnly(value);

        if (digits.Length > length)
        {
            if (!allowTruncate)
                throw new InvalidOperationException(
                    $"Numeric value '{digits}' (length {digits.Length}) does not fit field at {start} (length {length}).");
            digits = digits[..length];
        }

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

    /// <summary>Case folding applied by <see cref="Sanitize"/>: upper (CNAB default), lower (PIX key), or
    /// preserve (case-sensitive content written byte-exact, e.g. the PIX QR payload URL).</summary>
    private enum CnabCase { Upper, Lower, Preserve }

    /// <summary>Strip diacritics + fold case so the result is Latin-1 / ASCII-safe. Upper-cases by
    /// default (CNAB convention); lower-cases for the PIX key; preserves case for the QR URL.</summary>
    private static string Sanitize(string? value, CnabCase casing = CnabCase.Upper)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // drop combining accents
            sb.Append(casing switch
            {
                CnabCase.Lower => char.ToLowerInvariant(ch),
                CnabCase.Preserve => ch,
                _ => char.ToUpperInvariant(ch),
            });
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
