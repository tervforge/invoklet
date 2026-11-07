using System.Text;
using Invoklet.Format.MdRun;
using Invoklet.Services.Examples;
using Invoklet.Services.Recovery;
using Invoklet.UserControls.Models;

namespace Invoklet.Services.Tabs;

/// <summary>Where the script of a tab comes from when the tab opens.</summary>
internal enum ScriptSource
{
    /// <summary>A draft recovered from an earlier session.</summary>
    Recovered,

    /// <summary>An untitled copy of an example.</summary>
    Example,

    /// <summary>A new, untitled script.</summary>
    New,

    /// <summary>The script's file.</summary>
    File,

    /// <summary>A path whose file is not there.</summary>
    Missing,
}

/// <summary>
/// What a tab opens with: the text for the editor, the document around it, why the file is not a valid document when it is not
/// (then the text is the whole file, so a save writes it back as it was), and what is known of the file on disk, which later saves
/// are compared with. <see cref="DiskState"/> null means nothing is known; <see cref="RequiresBaselineConfirmation"/> that a recovered
/// draft does not know which version of its file it was made from.
/// </summary>
internal sealed record OpenedScript(
    ScriptSource Source,
    string Text,
    MdRunDocument? Document,
    string? ParseError,
    ScriptFileState? DiskState,
    bool RequiresBaselineConfirmation);

/// <summary>Decides what a tab opens with. The disk is reached only through the functions given, so the decision is tested without one.</summary>
internal static class ScriptOpening
{
    /// <summary>
    /// A recovered draft wins over its file; a tab without a path is an example's copy or a new script; a path is read, or is missing.
    /// </summary>
    public static OpenedScript Open(
        RecoveryTab? recovered,
        string? filePath,
        ExampleScript? example,
        MdRunLanguage newLanguage,
        MdRunKind newKind,
        Func<string> newCode,
        Func<string, bool> fileExists,
        Func<string, byte[]> readFile)
    {
        if (recovered is not null)
        {
            return new OpenedScript(
                ScriptSource.Recovered,
                recovered.EditorText,
                recovered.Document is null ? null : MdRunDocument.FromSnapshot(recovered.Document),
                recovered.ParseError,
                recovered.BaselineKnown ? new ScriptFileState(recovered.DiskHash?.ToArray()) : null,
                !recovered.BaselineKnown);
        }

        if (filePath is null && example is not null)
        {
            // The whole document of the example: the code, its front matter and the Markdown around it, which Save As writes out.
            var document = MdRunParser.Parse(example.Text).Document;
            return new OpenedScript(ScriptSource.Example, document?.RunCode ?? "", document, null, null, false);
        }

        if (filePath is null)
        {
            // In memory until the first save, but with a document of the chosen language and kind from the start, so the toolbar,
            // the analysis and Run already behave as for a saved file.
            var document = MdRunParser.TryParse(MdRunWriter.NewFile("", title: "Untitled", language: newLanguage, kind: newKind));
            return new OpenedScript(ScriptSource.New, newCode(), document, null, null, false);
        }

        if (fileExists(filePath)) return Read(readFile(filePath));

        // A file that appears later was made by someone else: saving must not replace it unasked.
        return new OpenedScript(ScriptSource.Missing, "", null, null, ScriptFileState.Absent, false);
    }

    /// <summary>The script in a file's exact bytes, which become what later saves compare the file with.</summary>
    public static OpenedScript Read(byte[] bytes)
    {
        var raw = Decode(bytes);
        var parsed = MdRunParser.Parse(raw);
        return new OpenedScript(ScriptSource.File, parsed.Document?.RunCode ?? raw, parsed.Document, parsed.Error, ScriptFileState.Of(bytes), false);
    }

    // As File.ReadAllText decodes.
    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
