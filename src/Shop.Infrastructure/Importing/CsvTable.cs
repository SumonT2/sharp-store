using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Shop.Infrastructure.Importing;

/// <summary>
/// A CSV file read into memory with case-insensitive headers, standard-field aliases and an optional
/// per-supplier column map (standard field → the supplier's own header).
/// </summary>
public sealed class CsvTable
{
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<CsvRow> Rows { get; }
    private readonly Dictionary<string, int> _fieldIndex;

    private CsvTable(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows, Dictionary<string, int> fieldIndex)
    {
        Headers = headers;
        Rows = rows;
        _fieldIndex = fieldIndex;
    }

    /// <summary>Whether a column for this standard field was found.</summary>
    public bool HasField(string field) => _fieldIndex.ContainsKey(field);

    public static CsvTable Read(
        Stream stream,
        IReadOnlyDictionary<string, string[]> aliases,
        IReadOnlyDictionary<string, string>? columnMap = null,
        int maxRows = 100_000)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t", "|"],
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } rawHeaders)
            throw new InvalidDataException("The file is empty or has no header row.");

        var headers = rawHeaders.Select(h => h.Trim()).ToArray();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
            index.TryAdd(headers[i], i);

        // Resolve each standard field to a column index: explicit supplier mapping first, then aliases.
        var fieldIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, names) in aliases)
        {
            if (columnMap is not null && columnMap.TryGetValue(field, out var mapped) && index.TryGetValue(mapped, out var mi))
            {
                fieldIndex[field] = mi;
                continue;
            }
            foreach (var name in names.Prepend(field))
            {
                if (index.TryGetValue(name, out var ai))
                {
                    fieldIndex[field] = ai;
                    break;
                }
            }
        }

        var rows = new List<CsvRow>();
        while (csv.Read())
        {
            if (rows.Count >= maxRows)
                throw new InvalidDataException($"The file has more than {maxRows:N0} rows. Split it into smaller files.");

            var values = new string[headers.Length];
            var empty = true;
            for (var i = 0; i < headers.Length; i++)
            {
                values[i] = csv.TryGetField<string>(i, out var v) ? v?.Trim() ?? "" : "";
                if (values[i].Length > 0)
                    empty = false;
            }
            if (!empty)
                rows.Add(new CsvRow(csv.Parser.RawRow, values, fieldIndex, index));
        }

        return new CsvTable(headers, rows, fieldIndex);
    }

    /// <summary>Parses "cost=Unit Price; stock=Qty" (or one mapping per line).</summary>
    public static Dictionary<string, string> ParseColumnMap(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return map;
        foreach (var part in text.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return map;
    }
}

public sealed class CsvRow(int lineNumber, string[] values, Dictionary<string, int> fieldIndex, Dictionary<string, int> headerIndex)
{
    public int LineNumber { get; } = lineNumber;

    public bool Has(string field) => fieldIndex.ContainsKey(field);

    /// <summary>Value of a standard field, or null when the column is missing or the cell is empty.</summary>
    public string? Get(string field) =>
        fieldIndex.TryGetValue(field, out var i) && values[i].Length > 0 ? values[i] : null;

    /// <summary>Value by literal header name.</summary>
    public string? GetByHeader(string header) =>
        headerIndex.TryGetValue(header, out var i) && values[i].Length > 0 ? values[i] : null;
}
