namespace Invoklet.Plugins.Hosting;

/// <summary>Extracts the host's development switch before forwarding the rest of the command line.</summary>
public sealed record PluginDevelopmentOptions(string? Directory, IReadOnlyList<string> RemainingArguments)
{
    public const string EnvironmentVariable = "INVOKLET_PLUGINS_DEV";

    public static PluginDevelopmentOptions Parse(IReadOnlyList<string> arguments, string? environmentValue)
    {
        const string option = "--plugins-dev";
        var remaining = new List<string>();
        string? directory = null;
        var optionsEnded = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!optionsEnded && argument == option)
            {
                if (directory is not null) throw new ArgumentException($"{option} may only be specified once.");
                if (++index == arguments.Count || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{option} requires a directory.");
                directory = arguments[index];
                continue;
            }
            if (argument == "--") optionsEnded = true;
            remaining.Add(argument);
        }

        directory ??= string.IsNullOrWhiteSpace(environmentValue) ? null : environmentValue;
        return new PluginDevelopmentOptions(directory is null ? null : Path.GetFullPath(directory), remaining.AsReadOnly());
    }
}
