namespace Invoklet.Services.Preferences;

// A settings bag per language that has its own settings to hold, same shape as DebuggerSettings —
// not a generic "per-language settings" framework (that's
// a bigger, still-open task; this is just the first concrete language settings bag, for the one
// language that has a setting worth exposing today).
public sealed class CSharpLanguageSettings
{
    // Inferred `var x`/`foreach (var x in ...)` type annotations
    // (Rider/VS Code style). Off unless turned on: in a scratchpad most `var`s are short-lived and
    // their types obvious, so the inline decorations are more distracting than useful by default.
    public bool InlayHintsEnabled { get; set; }

    // Master switch for the lightbulb margin's Roslyn code
    // fixes/refactorings (RoslynCodeFixEngine). Diagnostics themselves (the squiggles) are unaffected
    // — this only gates the FIX suggestions layered on top, for users who find the lightbulb noisy.
    public bool CodeFixesEnabled { get; set; } = true;

    // Exact, case-insensitive Roslyn
    // diagnostic ID exclusion list ("CS0184", "IDE0007", "IDE0055", ...) — lets a user silence
    // specific opinionated suggestions without turning code fixes off entirely. A diagnostic ID
    // instead of a Title substring (the original design): stable and documented,
    // where a title is free-form prose the provider can phrase however it likes. Only applies to
    // diagnostic-driven fixes — a CodeRefactoringProvider's suggestion has no underlying diagnostic
    // to key off (ICodeFixInfo.DiagnosticId's own doc comment), so it can't be excluded this way.
    //
    // IDE0055 ("Fix formatting") ships excluded by default: Invoklet already
    // has its own `dotnet format`-based CI gate and editor formatting (Ctrl+Format), so a live
    // "reformat this" suggestion firing on every whitespace deviation while typing was judged more
    // noise than help for most users — default to what most users want, not to the maximal feature set.
    public List<string> ExcludedDiagnosticIds { get; set; } = ["IDE0055"];
}
