using System.Collections;
using System.Reflection;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Serialization;
using Stride.Engine;

namespace Stride.Avalonia.Editor.Controls.Serialization;

/// <summary>
/// Reflects on Stride <see cref="EntityComponent"/> instances to produce a list
/// of <see cref="EditableProperty"/> descriptors.
/// <para>
/// The inspector honours standard Stride attributes:
/// <list type="bullet">
///   <item><c>[DataMember(order)]</c> – sort order and opt-in serialisation.</item>
///   <item><c>[DataMemberIgnore]</c> – exclude from the editor.</item>
///   <item><c>[Display("Label", "Category")]</c> / Stride DisplayAttribute.</item>
///   <item><c>[DataMemberRange(min, max, smallStep, largeStep, decimalPlaces)]</c> – numeric constraints.</item>
/// </list>
/// Components that implement <see cref="IEditableComponent"/> can override
/// this entirely or supplement the reflected list.
/// </para>
/// </summary>
public static class ComponentInspector
{
    /// <summary>
    /// Types on base classes that should never appear in the properties panel.
    /// </summary>
    private static readonly HashSet<string> IgnoredPropertyNames = new(StringComparer.Ordinal)
    {
        "Entity",
        "ECSGroup",
        "Id",
        "ImmutableId",
        "Tags",
    };

    private static readonly HashSet<Type> IgnoredDeclaringTypes =
    [
        typeof(EntityComponent),
        typeof(object),
    ];

    /// <summary>
    /// Build the editable property list for a component.
    /// If the component implements <see cref="IEditableComponent"/> and returns
    /// a non-null list, that list is used directly. Otherwise we reflect.
    /// </summary>
    public static IReadOnlyList<EditableProperty> GetProperties(EntityComponent component)
    {
        if (component is IEditableComponent editable)
        {
            var custom = editable.GetEditableProperties();
            if (custom is not null)
                return custom;
        }

        return ReflectProperties(component.GetType());
    }

    /// <summary>
    /// Returns the display name for a component in the inspector header.
    /// </summary>
    public static string GetDisplayName(EntityComponent component)
    {
        if (component is IEditableComponent { DisplayName: { } name })
            return name;

        return component.GetType().Name;
    }

    // ── Reflection engine ────────────────────────────────

    private static IReadOnlyList<EditableProperty> ReflectProperties(Type componentType)
    {
        var result = new List<EditableProperty>();

        var props = componentType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in props)
        {
            if (ShouldSkip(prop))
                continue;

            var descriptor = BuildDescriptor(prop);
            result.Add(descriptor);
        }

        result.Sort((a, b) => a.Order.CompareTo(b.Order));
        return result;
    }

    private static bool ShouldSkip(PropertyInfo prop)
    {
        // Skip indexers
        if (prop.GetIndexParameters().Length > 0)
            return true;

        // Skip base-class noise
        if (prop.DeclaringType is not null && IgnoredDeclaringTypes.Contains(prop.DeclaringType))
            return true;

        if (IgnoredPropertyNames.Contains(prop.Name))
            return true;

        // Skip [DataMemberIgnore]
        if (prop.GetCustomAttribute<DataMemberIgnoreAttribute>() is not null)
            return true;

        return false;
    }

    private static EditableProperty BuildDescriptor(PropertyInfo prop)
    {
        var dataMember = prop.GetCustomAttribute<DataMemberAttribute>();
        var display = prop.GetCustomAttribute<DisplayAttribute>();
        var range = prop.GetCustomAttribute<DataMemberRangeAttribute>();

        var displayName = display?.Name ?? SplitPascalCase(prop.Name);
        var category = display?.Category;
        string? description = null; // Stride DisplayAttribute has no Description property
        var order = dataMember?.Order ?? 0;

        var propertyType = ResolvePropertyType(prop.PropertyType);
        var isReadOnly = !prop.CanWrite || prop.GetSetMethod() is null;

        Type? elementType = null;
        string[]? enumValues = null;

        if (propertyType == EditablePropertyType.Enum)
        {
            enumValues = Enum.GetNames(prop.PropertyType);
        }
        else if (propertyType is EditablePropertyType.List)
        {
            elementType = GetCollectionElementType(prop.PropertyType);
        }
        else if (propertyType is EditablePropertyType.Dictionary)
        {
            elementType = prop.PropertyType.IsGenericType
                ? prop.PropertyType.GetGenericArguments().LastOrDefault()
                : null;
        }
        else if (propertyType is EditablePropertyType.AssetReference)
        {
            elementType = GetUrlReferenceType(prop.PropertyType);
        }

        // Range constraints
        double? min = null, max = null, small = null, large = null;
        int? decimals = null;
        ExtractRange(range, ref min, ref max, ref small, ref large, ref decimals);

        return new EditableProperty
        {
            Name = prop.Name,
            DisplayName = displayName,
            Description = description,
            Category = category,
            Order = order,
            PropertyType = propertyType,
            ClrType = prop.PropertyType,
            ElementType = elementType,
            EnumValues = enumValues,
            Minimum = min,
            Maximum = max,
            SmallStep = small,
            LargeStep = large,
            DecimalPlaces = decimals,
            IsReadOnly = isReadOnly,
            PropertyInfo = prop,
        };
    }

    // ── Type mapping ─────────────────────────────────────

    private static EditablePropertyType ResolvePropertyType(Type type)
    {
        // Primitives
        if (type == typeof(string)) return EditablePropertyType.String;
        if (type == typeof(int)) return EditablePropertyType.Int;
        if (type == typeof(float)) return EditablePropertyType.Float;
        if (type == typeof(double)) return EditablePropertyType.Double;
        if (type == typeof(bool)) return EditablePropertyType.Bool;

        // Stride math types
        if (type == typeof(Stride.Core.Mathematics.Vector2)) return EditablePropertyType.Vector2;
        if (type == typeof(Stride.Core.Mathematics.Vector3)) return EditablePropertyType.Vector3;
        if (type == typeof(Stride.Core.Mathematics.Vector4)) return EditablePropertyType.Vector4;
        if (type == typeof(Stride.Core.Mathematics.Quaternion)) return EditablePropertyType.Quaternion;
        if (type == typeof(Stride.Core.Mathematics.Color)) return EditablePropertyType.Color;
        if (type == typeof(Stride.Core.Mathematics.Color3)) return EditablePropertyType.Color3;
        if (type == typeof(Stride.Core.Mathematics.Color4)) return EditablePropertyType.Color4;

        // Enums
        if (type.IsEnum) return EditablePropertyType.Enum;

        // Stride asset references
        if (IsUrlReference(type)) return EditablePropertyType.AssetReference;
        if (type == typeof(Prefab)) return EditablePropertyType.Prefab;

        // Entity / component references
        if (type == typeof(Entity)) return EditablePropertyType.EntityReference;
        if (typeof(EntityComponent).IsAssignableFrom(type)) return EditablePropertyType.ComponentReference;

        // Collections
        if (IsDictionaryType(type)) return EditablePropertyType.Dictionary;
        if (IsListType(type)) return EditablePropertyType.List;

        // Known Stride component types that are asset-like references
        if (typeof(Stride.Engine.ISpriteProvider).IsAssignableFrom(type)) return EditablePropertyType.AssetReference;

        // Nested complex object
        if (type.IsClass && type != typeof(string))
            return EditablePropertyType.Object;

        return EditablePropertyType.Unknown;
    }

    private static bool IsUrlReference(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.FullName?.StartsWith("Stride.Core.Serialization.UrlReference") == true;
    }

    private static Type? GetUrlReferenceType(Type type)
    {
        if (type.IsGenericType)
            return type.GetGenericArguments().FirstOrDefault();
        return null;
    }

    private static bool IsListType(Type type)
    {
        if (type.IsArray) return true;
        if (!type.IsGenericType) return false;
        return typeof(IList).IsAssignableFrom(type)
            || typeof(ICollection<>).MakeGenericType(type.GetGenericArguments()).IsAssignableFrom(type);
    }

    private static bool IsDictionaryType(Type type)
    {
        if (!type.IsGenericType) return false;
        return typeof(IDictionary).IsAssignableFrom(type);
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        if (type.IsGenericType)
            return type.GetGenericArguments().FirstOrDefault();
        return null;
    }

    // ── Range extraction ─────────────────────────────────

    private static void ExtractRange(
        DataMemberRangeAttribute? range,
        ref double? min, ref double? max,
        ref double? small, ref double? large,
        ref int? decimals)
    {
        if (range is null) return;

        // DataMemberRangeAttribute stores values in its constructor params,
        // which are accessible via the Minimum/Maximum/SmallStep/LargeStep/DecimalPlaces properties.
        try
        {
            var rangeType = range.GetType();

            var minProp = rangeType.GetProperty("Minimum");
            var maxProp = rangeType.GetProperty("Maximum");
            var smallProp = rangeType.GetProperty("SmallStep");
            var largeProp = rangeType.GetProperty("LargeStep");
            var decProp = rangeType.GetProperty("DecimalPlaces");

            if (minProp?.GetValue(range) is double minVal) min = minVal;
            if (maxProp?.GetValue(range) is double maxVal) max = maxVal;
            if (smallProp?.GetValue(range) is double smallVal) small = smallVal;
            if (largeProp?.GetValue(range) is double largeVal) large = largeVal;
            if (decProp?.GetValue(range) is int decVal) decimals = decVal;
        }
        catch
        {
            // Attribute structure may vary across Stride versions – fail gracefully.
        }
    }

    // ── Utilities ────────────────────────────────────────

    private static string SplitPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
