namespace Invoklet.Plugins.Hosting;

/// <summary>A registration failure whose message is suitable for the Plugins page.</summary>
public sealed class PluginRegistrationException(string userMessage, Exception? innerException = null)
    : Exception(userMessage, innerException);
