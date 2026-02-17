using System;
using global::Avalonia.Controls;
using Stride.Core;

namespace Stride.Avalonia;

/// <summary>
/// A convenience <see cref="AvaloniaPage"/> implementation that wraps an
/// existing Avalonia <see cref="Control"/> instance.
/// <para>
/// Use this when creating pages programmatically from code.
/// For editor-selectable pages, subclass <see cref="AvaloniaPage"/> directly
/// and decorate with <c>[DataContract("YourPageName")]</c>.
/// </para>
/// </summary>
[DataContract("DefaultAvaloniaPage")]
public sealed class DefaultAvaloniaPage : AvaloniaPage
{
    private Control? _control;

    /// <summary>
    /// Parameterless constructor required by Stride serialization.
    /// </summary>
    public DefaultAvaloniaPage() { }

    /// <summary>
    /// Creates a new <see cref="DefaultAvaloniaPage"/> wrapping the given control.
    /// </summary>
    public DefaultAvaloniaPage(Control control)
    {
        _control = control;
    }

    /// <inheritdoc />
    protected override Control CreateContent() =>
        _control ?? throw new InvalidOperationException(
            $"{nameof(DefaultAvaloniaPage)} was created without a Control. " +
            "Use a custom AvaloniaPage subclass for editor-created pages.");
}
