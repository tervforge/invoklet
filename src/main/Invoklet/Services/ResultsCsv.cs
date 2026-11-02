using System.Text;

namespace Invoklet.Services;

/// <summary>A table as CSV (RFC 4180): commas, CRLF, and quotes around a field that holds a comma, a quote or a line break.</summary>
internal static class ResultsCsv
{
    public static string Build(ResultsTable table)
    {
        var csv = new StringBuilder();
        foreach (var row in table.Rows)
            csv.AppendJoin(',', row.Select(Field)).Append("\r\n");
        return csv.ToString();
    }

    // A field that starts with = + - @ is read as a formula by a spreadsheet, so a cell of data could run one; a quote in front keeps it text.
    private static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' || value.Length > 1 && value[0] == '-' && !double.TryParse(value, out _))
            value = "'" + value;
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
