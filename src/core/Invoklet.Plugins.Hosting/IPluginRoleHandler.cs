namespace Invoklet.Plugins.Hosting;

/// <summary>One plugin's registration of one role: the accepted context and, for a role with code, the plugin's entry instance.</summary>
/// <param name="Plugin">Null for a data role (a theme): the handler reads the plugin's folder instead.</param>
public sealed record PluginRegistration(PluginContext Context, IInvokletPlugin? Plugin);

/// <summary>
/// How the application takes in one role: the connection handler puts drivers in the driver registry, the theme handler reads theme
/// files, and so on. The loader knows no role itself; it calls the handler of each role a plugin declares, in
/// <see cref="PluginRoles.RegistrationOrder"/>. Preparing a registration must leave the host unchanged.
/// </summary>
public interface IPluginRoleHandler
{
    PluginRole Role { get; }

    /// <summary>The contract version this handler implements; a manifest naming another is rejected before loading.</summary>
    int ContractVersion { get; }

    PluginRoleRegistration Prepare(PluginRegistration registration);
}

/// <summary>An update to a role registry. Rollback must undo this update, including a partially failed Apply, without throwing.</summary>
public sealed record PluginRoleRegistration(Action Apply, Action Rollback);
