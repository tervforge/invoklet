using AvaloniaEdit.CodeCompletion;
using Invoklet.Avalonia.CodeTextEditor.Completions;
using Invoklet.Contracts.IntelliSense;

namespace Invoklet.Services;

/// <summary>Turns an item of the language service into the row the editor's completion window shows; builds controls.</summary>
internal interface ICompletionItemFactory
{
    ICompletionData Create(ICompletionItem item);
}

/// <summary>
/// Answers the editor's completion requests from the language service: what the request asks for, what to do
/// when the service finds the place not worth completing at, and the items otherwise.
/// </summary>
internal sealed class EditorCompletionProvider(
    IIntelliSenseService intelliSense, Func<string> text, ICompletionItemFactory items,
    Func<Task>? contextRefresh = null)
{
    public async Task ProvideAsync(CompletionRequestedEventArgs e)
    {
        var code = text();

        // A genuine Ctrl+Space (not the automatic triggers, which also set IsExplicitInvoke but pass the typed
        // character as Trigger, never '\0') in a fresh, not-yet-typed call argument asks for parameter info, not a
        // completion list, as in Rider and VS. Gated on the literal hotkey so this extra signature-help probe
        // never runs on the hot path of typing.
        if (e.IsExplicitInvoke && e.Trigger == '\0')
        {
            var signatureHelp = await intelliSense.GetSignatureHelpAsync(code, e.CaretOffset, cancellationToken: e.CancellationToken);
            if (signatureHelp is { IsArgumentSlotEmpty: true })
            {
                e.RedirectToOverloads = true;
                return;
            }
        }

        var trigger = e.IsExplicitInvoke ? CompletionTrigger.Invoked : CompletionTrigger.Insertion(e.Trigger);
        var refresh = e.Trigger == '.' || (e.Trigger == '\0' && e.CaretOffset > 0
            && e.CaretOffset <= code.Length && code[e.CaretOffset - 1] == '.')
            ? contextRefresh?.Invoke()
            : null;
        var refreshWasPending = refresh is { IsCompleted: false };
        var list = await intelliSense.GetCompletionsAsync(code, e.CaretOffset, trigger, e.CancellationToken);
        // Keep warm completion immediate. An unresolved receiver can be retried once the latest
        // already scheduled context has installed its references and generated connection types.
        if (list is null && refreshWasPending && contextRefresh is { } getRefresh)
        {
            while (true)
            {
                refresh = getRefresh();
                await refresh.WaitAsync(e.CancellationToken);
                if (text() != code) break;
                if (refresh != getRefresh()) continue;
                list = await intelliSense.GetCompletionsAsync(code, e.CaretOffset, trigger, e.CancellationToken);
                break;
            }
        }
        if (list is null)
        {
            // The service found this place not worth completing at (inside a comment or a string literal, for
            // one), so the snippets must not fill the list either.
            e.SuppressSnippets = true;
            return;
        }

        foreach (var item in list.Items) e.Items.Add(items.Create(item));
    }
}
