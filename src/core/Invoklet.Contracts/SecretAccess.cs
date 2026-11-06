namespace Invoklet.Contracts;

/// <summary>A running script asking for a secret: the script's file (null for one not saved yet), the secret's name, whether it saves one,
/// and whether that save replaces or removes a secret already kept.</summary>
public sealed record SecretAccess(string? ScriptPath, string Name, bool IsSave, SecretChange Change = SecretChange.None);

/// <summary>What a script's save does to a secret already in the store.</summary>
public enum SecretChange
{
    /// <summary>Nothing was kept under the name: a new secret.</summary>
    None,

    /// <summary>The value kept under the name is replaced.</summary>
    Replace,

    /// <summary>The secret kept under the name is removed.</summary>
    Remove,
}
