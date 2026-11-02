using System.Reflection;
using System.Text.Json.Serialization;

namespace Invoklet.Services.Preferences;

internal static class SettingsDefaults
{
    /// <summary>
    /// Replaces the null a settings file holds where a setting group or a list should be (<c>"Logging": null</c> is valid JSON and the serializer
    /// takes it literally) by what a missing one would have been, here and in the groups inside the groups: the rest of Invoklet reads them as never null.
    /// </summary>
    public static void FillNulls(object settings)
    {
        foreach (var property in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0
                || property.PropertyType.IsValueType || property.PropertyType == typeof(string)
                || property.IsDefined(typeof(JsonExtensionDataAttribute))) continue;
            var value = property.GetValue(settings);
            if (value is null)
            {
                if (property.PropertyType.GetConstructor(Type.EmptyTypes) is null) continue;
                value = Activator.CreateInstance(property.PropertyType);
                property.SetValue(settings, value);
            }
            if (value is not null && property.PropertyType.Namespace == typeof(AppSettings).Namespace) FillNulls(value);
        }
    }
}
