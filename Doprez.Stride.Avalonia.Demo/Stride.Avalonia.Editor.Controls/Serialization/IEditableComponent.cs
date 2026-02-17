namespace Stride.Avalonia.Editor.Controls.Serialization;

/// <summary>
/// Optional interface that components can implement to customise how they
/// appear in the Avalonia properties panel.
/// <para>
/// If a component does <b>not</b> implement this interface,
/// <see cref="ComponentInspector"/> will auto-discover editable properties
/// via reflection and Stride attributes (<c>[DataMember]</c>,
/// <c>[Display]</c>, <c>[DataMemberRange]</c>, etc.).
/// </para>
/// <para>
/// Implementing this interface lets a component:
/// <list type="bullet">
///   <item>Hide properties that reflection would normally expose.</item>
///   <item>Add virtual / computed properties that have no backing CLR property.</item>
///   <item>Override display names, categories, ranges, or property types.</item>
///   <item>Control property ordering and grouping.</item>
///   <item>Supply a custom validation callback.</item>
/// </list>
/// </para>
/// </summary>
public interface IEditableComponent
{
    /// <summary>
    /// Returns the list of properties that should be shown in the editor
    /// for this component instance.
    /// <para>
    /// Return <c>null</c> to fall back to the default reflection-based discovery.
    /// </para>
    /// </summary>
    IReadOnlyList<EditableProperty>? GetEditableProperties();

    /// <summary>
    /// Called by the editor after a property value is changed via the UI.
    /// Use this to run validation, clamp values, or trigger side-effects.
    /// <para>
    /// Return <c>true</c> if the change is accepted, <c>false</c> to revert.
    /// </para>
    /// </summary>
    /// <param name="property">The property that was changed.</param>
    /// <param name="oldValue">The value before the edit.</param>
    /// <param name="newValue">The new value set by the user.</param>
    bool OnPropertyChanged(EditableProperty property, object? oldValue, object? newValue) => true;

    /// <summary>
    /// Human-readable display name for this component type in the inspector header.
    /// Return <c>null</c> to use the default type name.
    /// </summary>
    string? DisplayName => null;
}
