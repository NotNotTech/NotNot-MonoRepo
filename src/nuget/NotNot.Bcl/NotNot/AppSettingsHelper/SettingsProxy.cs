using System;
using System.Reflection;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// DispatchProxy-based interceptor that detects property setter calls.
/// Used for POCO settings types that don't implement ISettingsChangeAware.
/// </summary>
/// <typeparam name="TSettings">Settings interface type (must be public interface).</typeparam>
/// <remarks>
/// <para>
/// <b>Limitation</b>: Only intercepts top-level property setters. Nested object mutations
/// (e.g., <c>Settings.Theme.Mode = "dark"</c>) are NOT detected because the getter
/// returns the nested object, and the setter is called on that object, not on the proxy.
/// </para>
/// <para>
/// For this reason, POCO settings used with DispatchProxy should have a flat structure
/// (primitive types and strings only, no nested objects).
/// </para>
/// </remarks>
public class SettingsProxy<TSettings> : DispatchProxy where TSettings : class
{
    private TSettings _target = null!;
    private Action? _onPropertyChanged;

    /// <summary>
    /// Creates a proxy wrapper around the settings object.
    /// </summary>
    /// <param name="target">The concrete settings instance to wrap.</param>
    /// <param name="onPropertyChanged">Callback invoked when any property setter is called.</param>
    /// <returns>A proxy that implements TSettings and intercepts property setters.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if TSettings is not a public interface.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown if target is null.
    /// </exception>
    public static TSettings Create(TSettings target, Action onPropertyChanged)
    {
        if (!typeof(TSettings).IsInterface)
        {
            throw new InvalidOperationException(
                $"SettingsProxy requires TSettings '{typeof(TSettings).Name}' to be an interface. " +
                $"DispatchProxy can only intercept interface method calls.");
        }

        // Check visibility using IsVisible (handles inherited visibility correctly)
        if (!typeof(TSettings).IsVisible)
        {
            throw new InvalidOperationException(
                $"SettingsProxy requires TSettings '{typeof(TSettings).Name}' to be a public interface. " +
                $"DispatchProxy cannot intercept non-public interfaces. " +
                $"If using internal interfaces, add [assembly: InternalsVisibleTo(\"ProxyBuilder\")].");
        }

        // Validate flat structure (SME P0 blocker fix - prevent silent nested mutation failures)
        ValidateFlatStructure();

        var proxy = Create<TSettings, SettingsProxy<TSettings>>();
        var settingsProxy = (SettingsProxy<TSettings>)(object)proxy;
        settingsProxy._target = target ?? throw new ArgumentNullException(nameof(target));
        settingsProxy._onPropertyChanged = onPropertyChanged;
        return proxy;
    }

    /// <summary>
    /// Validates that TSettings has a flat structure (no nested reference types).
    /// Throws if nested types are detected that would cause silent mutation failures.
    /// </summary>
    private static void ValidateFlatStructure()
    {
        foreach (var prop in typeof(TSettings).GetProperties())
        {
            var type = prop.PropertyType;

            // Allow primitives, strings, value types, and nullable value types
            if (type.IsPrimitive || type == typeof(string) || type.IsValueType)
                continue;

            // Allow nullable reference types that are primitives underneath
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null && (underlying.IsPrimitive || underlying == typeof(string) || underlying.IsValueType))
                continue;

            // Disallow reference types (classes, interfaces) - they cause silent mutation failures
            throw new InvalidOperationException(
                $"SettingsProxy requires flat structure. Property '{prop.Name}' " +
                $"of type '{type.Name}' is a reference type. Nested object mutations " +
                $"bypass the proxy and won't trigger change detection. " +
                $"Use only primitive types, strings, or value types for proxy-based settings.");
        }
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            return null;

        var result = targetMethod.Invoke(_target, args);

        // Property setters are compiled as methods named "set_PropertyName"
        if (targetMethod.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            _onPropertyChanged?.Invoke();
        }

        return result;
    }
}
