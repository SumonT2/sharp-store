using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shop.Core.Text;

public static partial class TextUtil
{
    public static string Slugify(string input)
    {
        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == '+')
                sb.Append("plus");
            else
                sb.Append('-');
        }
        var slug = MultiDash().Replace(sb.ToString(), "-").Trim('-');
        return slug.Length == 0 ? "item" : slug.Length > 120 ? slug[..120].TrimEnd('-') : slug;
    }

    /// <summary>
    /// Parses prices like "1,299.00", "৳ 1,299", "1.299,50", "$12" or "CNY 89.5".
    /// When both separators appear, the last one is the decimal separator.
    /// A lone separator followed by exactly three digits is treated as a thousands separator.
    /// </summary>
    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = NumberToken().Match(text);
        if (!match.Success)
            return null;

        var raw = match.Value;
        var negative = text.TrimStart().StartsWith('-');
        var lastDot = raw.LastIndexOf('.');
        var lastComma = raw.LastIndexOf(',');

        string cleaned;
        if (lastDot >= 0 && lastComma >= 0)
        {
            var decimalSep = lastDot > lastComma ? '.' : ',';
            var thousandsSep = decimalSep == '.' ? ',' : '.';
            cleaned = raw.Replace(thousandsSep.ToString(), "").Replace(decimalSep, '.');
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var parts = raw.Split(sep);
            var isThousands = parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3 && sep == ',');
            cleaned = isThousands ? raw.Replace(sep.ToString(), "") : raw.Replace(sep, '.');
        }
        else
        {
            cleaned = raw;
        }

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;
        return negative ? -value : value;
    }

    public static int? ParseInt(string? text)
    {
        var d = ParseDecimal(text);
        return d is null ? null : (int)Math.Floor(d.Value);
    }

    public static bool? ParseBool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => null,
        };
    }

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex NumberToken();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
