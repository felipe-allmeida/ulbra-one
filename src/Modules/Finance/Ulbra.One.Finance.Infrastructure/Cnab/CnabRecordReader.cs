using System.Globalization;

namespace Ulbra.One.Finance.Infrastructure.Cnab;

/// <summary>
/// 1-based positional reader for a single fixed-width CNAB240 record.
/// Mirrors <see cref="CnabRecordBuilder"/> in the reverse direction: extracts typed values
/// from slices of a 240-char line using the same 1-based field positions defined in the layout.
/// </summary>
public sealed class CnabRecordReader
{
    private readonly string _line;

    /// <param name="line240">Decoded 240-char record string (Latin-1, CRLF already stripped).</param>
    public CnabRecordReader(string line240)
    {
        if (line240.Length != 240)
            throw new ArgumentException(
                $"CNAB240 record must be exactly 240 chars; got {line240.Length}.", nameof(line240));
        _line = line240;
    }

    /// <summary>Extract an alpha field (1-based). Returns the slice, trimmed of leading/trailing spaces.</summary>
    public string Alpha(int start, int length) => _line.Substring(start - 1, length).Trim();

    /// <summary>Extract a numeric field (1-based). Parses the right-justified zero-padded digits as <see langword="long"/>.</summary>
    public long Numeric(int start, int length)
    {
        var slice = _line.Substring(start - 1, length).Trim();
        if (slice.Length == 0 || slice.All(c => c == '0'))
            return 0L;
        return long.Parse(slice, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Extract a money field (1-based). The stored value has 2 implied decimals with no separator
    /// (e.g. <c>"000000000150000"</c> → <c>1500.00m</c>).
    /// </summary>
    public decimal Money(int start, int length)
    {
        var slice = _line.Substring(start - 1, length).Trim();
        if (slice.Length == 0)
            return 0m;
        var raw = long.Parse(slice, CultureInfo.InvariantCulture);
        return raw / 100m;
    }

    /// <summary>
    /// Extract a DDMMAAAA date field (1-based). Returns <see langword="null"/> if the field is
    /// all-zeros or all-spaces (blank on remessa — not yet filled by the bank).
    /// </summary>
    public DateOnly? Date(int start, int length)
    {
        if (length != 8)
            throw new ArgumentException("CNAB date fields are DDMMAAAA (length 8).", nameof(length));

        var slice = _line.Substring(start - 1, length);
        if (slice.All(c => c == '0' || c == ' '))
            return null;

        // Format: DDMMAAAA (day, month, year — non-standard but per CNAB240 spec).
        return DateOnly.ParseExact(slice, "ddMMyyyy", CultureInfo.InvariantCulture);
    }
}
