using Invoklet.UserControls.Models;

namespace Invoklet.Services.Tabs;

/// <summary>Keeps the released content of tabs somewhere that is not memory.</summary>
public interface ITabSpillStore
{
    /// <summary>False when it could not be written: the content must then stay where it is.</summary>
    bool TryWrite(Guid tab, ReleasedContent content);

    /// <summary><see cref="TryWrite"/> for a store that can wait without holding its caller's thread; a store on a local disk just writes.</summary>
    Task<bool> TryWriteAsync(Guid tab, ReleasedContent content) => Task.FromResult(TryWrite(tab, content));

    /// <summary>Null when it cannot be read.</summary>
    ReleasedContent? TryRead(Guid tab);

    void Delete(Guid tab);

    /// <summary>Everything this process kept.</summary>
    void DeleteAll();
}
