using System.Globalization;
using Mrp.SharedKernel.Domain;

namespace Mrp.Platform.Domain;

/// <summary>How often the running number restarts.</summary>
public enum NumberingPeriod
{
    /// <summary>Never restarts.</summary>
    None,

    /// <summary>Restarts every year; period text <c>yy</c>.</summary>
    Year,

    /// <summary>Restarts every month; period text <c>yyMM</c>.</summary>
    Month,
}

/// <summary>Per-tenant format of a document number, e.g. <c>PO-2610-0001</c>.</summary>
public sealed class DocumentNumberFormat : Entity
{
    private DocumentNumberFormat()
    {
    }

    /// <summary>Creates a format.</summary>
    public DocumentNumberFormat(string documentType, string prefix, NumberingPeriod period, int digits, string separator = "-")
    {
        DocumentType = documentType;
        Update(prefix, period, digits, separator);
    }

    /// <summary>Document type key such as <c>PO</c>.</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Text in front of the number.</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>Reset period.</summary>
    public NumberingPeriod Period { get; private set; }

    /// <summary>Minimum digits of the running number (zero padded).</summary>
    public int Digits { get; private set; }

    /// <summary>Separator between parts; may be empty.</summary>
    public string Separator { get; private set; } = "-";

    /// <summary>Default format for a type that the tenant has not configured.</summary>
    public static DocumentNumberFormat Default(string documentType) =>
        new(documentType, documentType.ToUpperInvariant(), NumberingPeriod.Month, 4);

    /// <summary>Changes the format.</summary>
    public void Update(string prefix, NumberingPeriod period, int digits, string separator)
    {
        if (digits is < 3 or > 10)
        {
            throw new DomainException("platform.numbering.invalid_digits", "Digits must be between 3 and 10.", 400);
        }

        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 10 || !prefix.All(char.IsLetterOrDigit))
        {
            throw new DomainException("platform.numbering.invalid_prefix", "Prefix must be 1-10 letters or digits.", 400);
        }

        if (separator is not ("" or "-" or "/"))
        {
            throw new DomainException("platform.numbering.invalid_separator", "Separator must be empty, '-' or '/'.", 400);
        }

        Prefix = prefix.ToUpperInvariant();
        Period = period;
        Digits = digits;
        Separator = separator;
    }

    /// <summary>Key of the running-number bucket for a local date.</summary>
    public string PeriodKey(DateTime localDate) => Period switch
    {
        NumberingPeriod.Year => localDate.ToString("yy", CultureInfo.InvariantCulture),
        NumberingPeriod.Month => localDate.ToString("yyMM", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    /// <summary>Builds the document number from a running number.</summary>
    public string Format(DateTime localDate, long running)
    {
        var parts = new List<string>(3) { Prefix };
        var period = PeriodKey(localDate);
        if (period.Length > 0)
        {
            parts.Add(period);
        }

        parts.Add(running.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0'));
        return string.Join(Separator, parts);
    }
}

/// <summary>Last issued running number per tenant, document type and period.</summary>
public sealed class DocumentSequence : ITenantOwned
{
    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Document type key.</summary>
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>Period bucket (<c>yyMM</c>, <c>yy</c> or empty).</summary>
    public string PeriodKey { get; private set; } = string.Empty;

    /// <summary>Last issued number.</summary>
    public long LastNumber { get; private set; }
}
