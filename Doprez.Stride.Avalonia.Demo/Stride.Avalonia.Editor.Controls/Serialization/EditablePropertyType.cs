namespace Stride.Avalonia.Editor.Controls.Serialization;

/// <summary>
/// Enumerates the property types that the editor knows how to render.
/// Used by <see cref="EditableProperty"/> to select the correct Avalonia control.
/// </summary>
public enum EditablePropertyType
{
    /// <summary>Single-line text (System.String).</summary>
    String,

    /// <summary>32-bit signed integer.</summary>
    Int,

    /// <summary>32-bit floating-point number.</summary>
    Float,

    /// <summary>64-bit floating-point number.</summary>
    Double,

    /// <summary>Boolean on/off toggle.</summary>
    Bool,

    /// <summary>Stride Vector2 (2 floats).</summary>
    Vector2,

    /// <summary>Stride Vector3 (3 floats).</summary>
    Vector3,

    /// <summary>Stride Vector4 (4 floats).</summary>
    Vector4,

    /// <summary>Stride Color (R, G, B, A as bytes).</summary>
    Color,

    /// <summary>Stride Color3 (R, G, B as floats).</summary>
    Color3,

    /// <summary>Stride Color4 (R, G, B, A as floats).</summary>
    Color4,

    /// <summary>Stride Quaternion (usually edited as Euler angles).</summary>
    Quaternion,

    /// <summary>Enum dropdown.</summary>
    Enum,

    /// <summary>Stride Prefab reference.</summary>
    Prefab,

    /// <summary>Stride UrlReference&lt;T&gt; asset pointer.</summary>
    AssetReference,

    /// <summary>Reference to another EntityComponent on any entity.</summary>
    ComponentReference,

    /// <summary>Reference to an Entity.</summary>
    EntityReference,

    /// <summary>Ordered collection (List&lt;T&gt;, array, etc.).</summary>
    List,

    /// <summary>Key-value collection (Dictionary&lt;K,V&gt;).</summary>
    Dictionary,

    /// <summary>A nested complex object that should be expanded inline.</summary>
    Object,

    /// <summary>Fallback – rendered as a read-only ToString() label.</summary>
    Unknown,
}
