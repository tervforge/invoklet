namespace Invoklet.Contracts;

/// <summary>A running script asking for a secret: the script's file (null for one not saved yet), the secret's name, and whether it saves one.</summary>
public sealed record SecretAccess(string? ScriptPath, string Name, bool IsSave);
