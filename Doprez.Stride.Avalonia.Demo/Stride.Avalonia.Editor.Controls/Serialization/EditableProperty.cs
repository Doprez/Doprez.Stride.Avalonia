using System.Reflection;

namespace Stride.Avalonia.Editor.Controls.Serialization;

/// <summary>
/// Serializable descriptor for a single editable property on a component.
/// Carries enough metadata for <see cref="PropertyControlFactory"/>
/// to create the right Avalonia control without re-reflecting at render time.
/// </summary>
public sealed class EditableProperty
{
    // ── Identity ─────────────────────────────────────────

    /// <summary>CLR property name (used for get/set via reflection).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human-readable label shown in the UI (falls back to <see cref="Name"/>).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Optional tooltip / description from <c>[Display]</c> or XML doc.</summary>
    public string? Description { get; init; }

    /// <summary>Grouping category (e.g. "Wave A", "LOD", "Visual").</summary>
    public string? Category { get; init; }

    /// <summary>Sort order within its category (maps to Stride <c>[DataMember(order)]</c>).</summary>
    public int Order { get; init; }

    // ── Typing ───────────────────────────────────────────

    /// <summary>High-level editor type that controls which Avalonia control is used.</summary>
    public EditablePropertyType PropertyType { get; init; } = EditablePropertyType.Unknown;

    /// <summary>The actual CLR <see cref="System.Type"/> of the property.</summary>
    public Type ClrType { get; init; } = typeof(object);

    /// <summary>For generic types like <c>List&lt;T&gt;</c> or <c>UrlReference&lt;T&gt;</c>,
    /// the inner element type.</summary>
    public Type? ElementType { get; init; }

    /// <summary>For <see cref="EditablePropertyType.Enum"/>, the available enum values.</summary>
    public string[]? EnumValues { get; init; }

    // ── Constraints ──────────────────────────────────────

    /// <summary>Minimum value (from <c>[DataMemberRange]</c>).</summary>
    public double? Minimum { get; init; }

    /// <summary>Maximum value (from <c>[DataMemberRange]</c>).</summary>
    public double? Maximum { get; init; }

    /// <summary>Small increment step (arrow keys / drag).</summary>
    public double? SmallStep { get; init; }

    /// <summary>Large increment step (page up/down).</summary>
    public double? LargeStep { get; init; }

    /// <summary>Number of decimal places to show.</summary>
    public int? DecimalPlaces { get; init; }

    // ── Access ────────────────────────────────────────────

    /// <summary>True if the property is read-only (no public setter or marked <c>[DataMemberIgnore]</c>).</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>The underlying <see cref="PropertyInfo"/> for get/set operations.</summary>
    public PropertyInfo? PropertyInfo { get; init; }

    // ── Value access helpers ─────────────────────────────

    /// <summary>Gets the current value from a component instance.</summary>
    public object? GetValue(object component) => PropertyInfo?.GetValue(component);

    /// <summary>Sets a new value on a component instance (no-op if read-only).</summary>
    public void SetValue(object component, object? value)
    {
        if (!IsReadOnly && PropertyInfo is { CanWrite: true })
            PropertyInfo.SetValue(component, value);
    }

    public override string ToString() =>
        $"{DisplayName} ({PropertyType}, {ClrType.Name})";
}
