using System.Net;
using System.Text;

namespace Invoklet.Services;

/// <summary>The results of a script as a page that opens in any browser.</summary>
internal static class ResultsHtml
{
    /// <summary>The stylesheet and the output as one document: UTF-8, titled, with the colours of the theme the results were drawn in.</summary>
    public static string Build(string css, string content, string title)
    {
        var page = new StringBuilder();
        page.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>")
            .Append(WebUtility.HtmlEncode(title))
            // A stylesheet that held "</style" would end itself early and let the rest be read as markup.
            .Append("</title>\n<style>\n").Append(css.Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase))
            .Append("\n</style>\n</head>\n<body>\n").Append(content).Append("\n</body>\n</html>\n");
        return page.ToString();
    }
}

/// <summary>Saves the results panel of a tab as an HTML file: asks where, writes it, and says so when there is nothing to save or the file cannot be written.</summary>
internal sealed class ResultsExporter(
    IWindowService windows, Func<string> css, Func<string> content, Func<string?> scriptPath, Func<string> startFolder,
    Func<string, string, CancellationToken, Task>? write = null, Func<string, string, CancellationToken, Task>? writeCsv = null)
{
    private readonly Func<string, string, CancellationToken, Task> _write = write ?? ((path, text, ct) => File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct));

    // With a byte order mark: Excel reads a CSV without one as the system's code page and garbles non-ASCII text.
    private readonly Func<string, string, CancellationToken, Task> _writeCsv = writeCsv ?? ((path, text, ct) => File.WriteAllTextAsync(path, text, new UTF8Encoding(true), ct));

    public async Task ExportAsync(CancellationToken ct = default)
    {
        var html = content();
        if (html.Length == 0)
        {
            await windows.ShowErrorAsync("Export Results", "There are no results to export.");
            return;
        }

        var (name, folder) = Target();
        var path = await windows.ShowSaveHtmlFileDialogAsync(name + ".html", folder);
        if (path is null) return;
        await WriteAsync(path, () => _write(path, ResultsHtml.Build(css(), html, name + " — results"), ct));
    }

    /// <summary>One table of the results, by its number among the tables not inside another (see <see cref="ResultsTables"/>), as a CSV file.</summary>
    public async Task ExportTableCsvAsync(int table, CancellationToken ct = default)
    {
        var tables = ResultsTables.Parse(content());
        if (table < 0 || table >= tables.Count)
        {
            await windows.ShowErrorAsync("Export Table", "This table is no longer in the results.");
            return;
        }

        var (name, folder) = Target();
        var path = await windows.ShowSaveCsvFileDialogAsync(name + (tables.Count > 1 ? "-" + (table + 1) : "") + ".csv", folder);
        if (path is null) return;
        await WriteAsync(path, () => _writeCsv(path, ResultsCsv.Build(tables[table]), ct));
    }

    // The script's name and folder, which the dialog offers; "Results" in the start folder for a script not saved yet.
    private (string Name, string Folder) Target()
    {
        var script = scriptPath();
        return (script is null ? "Results" : Path.GetFileNameWithoutExtension(script),
            script is null ? startFolder() : Path.GetDirectoryName(script) ?? startFolder());
    }

    private async Task WriteAsync(string path, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await windows.ShowErrorAsync("Export Results Failed", $"Could not write '{path}':\n{ex.Message}");
        }
    }
}
