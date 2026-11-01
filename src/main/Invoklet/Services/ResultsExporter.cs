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
    Func<string, string, CancellationToken, Task>? write = null, Func<string, byte[], CancellationToken, Task>? writeBytes = null)
{
    private readonly Func<string, string, CancellationToken, Task> _write = write ?? ((path, text, ct) => File.WriteAllTextAsync(path, text, new UTF8Encoding(false), ct));
    private readonly Func<string, byte[], CancellationToken, Task> _writeBytes = writeBytes ?? ((path, bytes, ct) => File.WriteAllBytesAsync(path, bytes, ct));

    public async Task ExportAsync(CancellationToken ct = default)
    {
        var html = content();
        if (html.Length == 0)
        {
            await NothingToExportAsync();
            return;
        }

        if (await ChooseAsync(".html", windows.ShowSaveHtmlFileDialogAsync) is not { } target) return;
        await WriteAsync(target.Path, () => _write(target.Path, ResultsHtml.Build(css(), html, target.Name + " — results"), ct));
    }

    /// <summary>Every table of the results as a sheet of an Excel workbook, the rest of the output as a last sheet.</summary>
    public async Task ExportExcelAsync(CancellationToken ct = default)
    {
        var sheets = ResultsTables.Parse(content());
        if (sheets.Count == 0)
        {
            await NothingToExportAsync();
            return;
        }

        if (await ChooseAsync(".xlsx", windows.ShowSaveExcelFileDialogAsync) is not { } target) return;
        await WriteAsync(target.Path, () =>
        {
            using var workbook = new MemoryStream();
            XlsxWriter.Write(workbook, sheets);
            return _writeBytes(target.Path, workbook.ToArray(), ct);
        });
    }

    private Task NothingToExportAsync() => windows.ShowErrorAsync("Export Results", "There are no results to export.");

    // The file to write and the script's name, or null when the dialog was cancelled.
    private async Task<(string Path, string Name)?> ChooseAsync(string extension, Func<string, string, Task<string?>> dialog)
    {
        var script = scriptPath();
        var name = script is null ? "Results" : Path.GetFileNameWithoutExtension(script);
        var folder = script is null ? startFolder() : Path.GetDirectoryName(script) ?? startFolder();
        return await dialog(name + extension, folder) is { } path ? (path, name) : null;
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
