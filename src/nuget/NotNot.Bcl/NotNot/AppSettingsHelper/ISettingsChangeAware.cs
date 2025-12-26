using System;
using System.ComponentModel;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// Interface for settings objects that can notify when their properties change.
/// <para>
/// This interface is implemented by source-generated settings classes.
/// It allows <see cref="AppSettingsManager{TSettings}"/> to wire up change
/// tracking for automatic save functionality.
/// </para>
/// <para>
/// This interface is not intended for direct use by consumers.
/// </para>
/// </summary>
/// <remarks>
/// The interface uses underscore-prefixed method names to minimize collision
/// with user-defined properties and to indicate internal usage.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISettingsChangeAware
{
    /// <summary>
    /// Sets the callback to be invoked when any property changes.
    /// The callback is propagated recursively to all nested settings objects.
    /// </summary>
    /// <param name="callback">
    /// The callback to invoke on property change, or null to clear the callback.
    /// </param>
    void _SetChangeCallback(Action? callback);
}
