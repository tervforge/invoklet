// Named and placed as .NET requires of a startup hook: a class StartupHook outside any namespace, with a static Initialize.
// Invoklet names this assembly in DOTNET_STARTUP_HOOKS only when it debugs a script: the script is started by Invoklet, the debugger
// attaches to it, and the script must not run a line of its own before the debugger has set its breakpoints. Invoklet then writes one
// line on the script's stdin, which lets it go on.
internal static class StartupHook
{
    private const string HooksEnvVar = "DOTNET_STARTUP_HOOKS";

    public static void Initialize()
    {
        // Taken out first, so a .NET program the script starts doesn't load it too and wait for a line of its own stdin.
        System.Environment.SetEnvironmentVariable(HooksEnvVar, Without(
            System.Environment.GetEnvironmentVariable(HooksEnvVar), System.IO.Path.GetFileName(typeof(StartupHook).Assembly.Location)));
        System.Console.In.ReadLine();
    }

    // The hooks without this assembly, found by its file name: its path may be written another way (macOS's /var is /private/var).
    internal static string? Without(string? hooks, string fileName)
    {
        if (string.IsNullOrEmpty(hooks)) return null;
        var others = System.Linq.Enumerable.Where(hooks!.Split(System.IO.Path.PathSeparator),
            hook => hook.Length > 0 && !string.Equals(System.IO.Path.GetFileName(hook), fileName, System.StringComparison.OrdinalIgnoreCase));
        var left = string.Join(System.IO.Path.PathSeparator.ToString(), others);
        return left.Length == 0 ? null : left;
    }
}
