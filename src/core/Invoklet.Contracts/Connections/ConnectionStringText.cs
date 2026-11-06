using System.Text;

namespace Invoklet.Contracts.Connections;

/// <summary>
/// One setting inside a connection string (<c>Host=…</c>, <c>Password=…</c>) that the connection form shows as a field of its own.
/// </summary>
/// <param name="Key">The key written for a setting the text doesn't have yet.</param>
/// <param name="Synonyms">Other keys the server takes for the same setting (<c>Server</c> for <c>Host</c>); the first found is edited.</param>
/// <param name="IsSecret">A password: the form offers to insert a secret.</param>
public sealed record ConnectionStringPart(string Key, string Label, IReadOnlyList<string>? Synonyms = null, string? Example = null, bool IsSecret = false)
{
    // Keys are compared as ADO.NET does: letter case and the spaces around them aside.
    internal bool Names(string key) =>
        string.Equals(key.Trim(), Key, StringComparison.OrdinalIgnoreCase)
        || (Synonyms ?? []).Any(synonym => string.Equals(key.Trim(), synonym, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A setting a way of signing in writes into the connection string, such as <c>Integrated Security=true</c>.</summary>
public sealed record ConnectionStringSetting(ConnectionStringPart Part, string Value);

/// <summary>
/// A way of signing in to the server (a password, a pgpass file, Windows): the fields it shows, by <see cref="ConnectionStringPart.Key"/>,
/// and the settings it writes. The connection string is all that is kept, so the way is read back from it.
/// </summary>
/// <param name="Defaults">What choosing the way puts in a field of it that is empty, such as the usual place of a password file.</param>
public sealed record ConnectionAuthMode(string Label, IReadOnlyList<string> Parts, IReadOnlyList<ConnectionStringSetting>? Settings = null, string? Hint = null,
    IReadOnlyList<ConnectionStringSetting>? Defaults = null)
{
    /// <summary>Whether signing in this way uses the field.</summary>
    public bool Shows(ConnectionStringPart part) => Parts.Any(key => string.Equals(key, part.Key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Reads and writes one setting of a <c>key=value;key=value</c> connection string, leaving the rest of the text as it was written: the keys
/// keep their letter case, and settings the form has no field for stay where they are. Values with <c>;</c> are quoted, as ADO.NET does.
/// </summary>
public static class ConnectionStringText
{
    /// <summary>The setting's value, unquoted; null when the text doesn't have it.</summary>
    public static string? Read(string text, ConnectionStringPart part) =>
        Segments(text).FirstOrDefault(s => s.Key is { } key && part.Names(key)) is { Key: not null } segment ? Unquote(segment.Value) : null;

    /// <summary>The text with the setting set to <paramref name="value"/>, or taken out when it is empty.</summary>
    public static string Write(string text, ConnectionStringPart part, string? value)
    {
        var segments = Segments(text).ToList();
        var index = segments.FindIndex(s => s.Key is { } key && part.Names(key));
        if (string.IsNullOrEmpty(value))
        {
            if (index < 0) return text;
            segments.RemoveAt(index);
        }
        else if (index >= 0)
        {
            segments[index] = segments[index] with { Value = Quote(value) };
        }
        else
        {
            segments.RemoveAll(s => s.Key is null && s.Value.Trim().Length == 0);
            segments.Add(new Segment(part.Key, Quote(value)));
        }
        return string.Join(";", segments.Select(s => s.Key is null ? s.Value : s.Key + "=" + s.Value));
    }

    /// <summary>
    /// The way of signing in the text uses: the first whose settings it has (switched on); else the first of the others that shows every
    /// field the text fills (a pgpass file named in the text); else the first. A text without a password reads as the first way, with the
    /// password left empty, which the server then looks up as it would anyway.
    /// </summary>
    public static ConnectionAuthMode? Mode(string text, IReadOnlyList<ConnectionAuthMode> modes, IReadOnlyList<ConnectionStringPart> parts)
    {
        if (modes.Count == 0) return null;
        if (modes.FirstOrDefault(m => m.Settings is { Count: > 0 } settings && settings.All(s => IsOn(Read(text, s.Part)))) is { } withSettings)
            return withSettings;
        var filled = parts.Where(p => !string.IsNullOrEmpty(Read(text, p))).ToList();
        return modes.FirstOrDefault(m => m.Settings is not { Count: > 0 } && filled.All(m.Shows)) ?? modes[0];
    }

    /// <summary>The text signing in the way of <paramref name="mode"/>: the fields and settings of the other ways taken out, its settings written.</summary>
    public static string WithMode(string text, ConnectionAuthMode mode, IReadOnlyList<ConnectionAuthMode> modes, IReadOnlyList<ConnectionStringPart> parts)
    {
        var shownByAny = parts.Where(p => modes.Any(m => m.Shows(p))).ToList();
        foreach (var part in shownByAny.Where(p => !mode.Shows(p))) text = Write(text, part, null);
        foreach (var setting in modes.Where(m => m != mode).SelectMany(m => m.Settings ?? [])) text = Write(text, setting.Part, null);
        foreach (var setting in mode.Settings ?? []) text = Write(text, setting.Part, setting.Value);
        foreach (var value in mode.Defaults ?? [])
            if (string.IsNullOrEmpty(Read(text, value.Part))) text = Write(text, value.Part, value.Value);
        return text;
    }

    // A switch written as true, yes or sspi; an empty or false one is off.
    private static bool IsOn(string? value) =>
        value is { Length: > 0 } && !value.Equals("false", StringComparison.OrdinalIgnoreCase) && !value.Equals("no", StringComparison.OrdinalIgnoreCase);

    // A piece between semicolons: its key as written (null for a piece without '=', kept as it is) and its value as written.
    private sealed record Segment(string? Key, string Value);

    private static IEnumerable<Segment> Segments(string text)
    {
        var start = 0;
        char? quote = null;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                var c = text[i];
                if (quote is { } open)
                {
                    if (c == open)
                    {
                        if (i + 1 < text.Length && text[i + 1] == open) i++; // a doubled quote inside the value
                        else quote = null;
                    }
                    continue;
                }
                if ((c == '"' || c == '\'') && text.AsSpan(start, i - start).Trim().EndsWith("=")) { quote = c; continue; }
                if (c != ';') continue;
            }
            var piece = text[start..i];
            start = i + 1;
            if (i == text.Length && piece.Length == 0) yield break;
            var equals = piece.IndexOf('=');
            yield return equals < 0 ? new Segment(null, piece) : new Segment(piece[..equals], piece[(equals + 1)..]);
        }
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\'') && trimmed[^1] == trimmed[0])
            return trimmed[1..^1].Replace(new string(trimmed[0], 2), trimmed[0].ToString());
        return trimmed;
    }

    private static string Quote(string value)
    {
        if (value.IndexOfAny([';', '"', '\'']) < 0 && value.Trim() == value) return value;
        return new StringBuilder("\"").Append(value.Replace("\"", "\"\"")).Append('"').ToString();
    }
}
