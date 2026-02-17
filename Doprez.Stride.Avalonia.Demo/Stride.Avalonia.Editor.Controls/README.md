# Stride.Avalonia.Editor.Controls

A library of specialised Avalonia controls for editing Stride engine types at runtime. Used by the `Stride.Avalonia.Editor` property inspector, but can also be used independently in any Avalonia-based Stride tooling.

## Features

### Property Control Factory
`PropertyControlFactory` is a static factory that maps `EditablePropertyType` values to the appropriate Avalonia control. It handles two-way data binding and notifies `IEditableComponent.OnPropertyChanged` when values change. Supported types:

| Type | Control | Description |
|------|---------|-------------|
| String | `TextBox` | Single-line text input |
| Int | `NumberBox` | Integer input with optional min/max/step |
| Float | `NumberBox` | Float input with configurable decimal places |
| Double | `NumberBox` | Double input with configurable decimal places |
| Bool | `ToggleSwitch` | On/off toggle |
| Vector2 | `Vector2Editor` | Two colour-coded fields (X=red, Y=green) |
| Vector3 | `Vector3Editor` | Three colour-coded fields (X=red, Y=green, Z=blue) |
| Vector4 | `Vector4Editor` | Four colour-coded fields (X=red, Y=green, Z=blue, W=purple) |
| Quaternion | `QuaternionEditor` | Euler angle display (Pitch/Yaw/Roll in degrees) |
| Color / Color3 / Color4 | `ColorEditor` | HSV colour wheel with hex input; supports alpha toggle and byte/float backing |
| Enum | `ComboBox` | Dropdown with all enum values |
| EntityReference | `EntityReferencePicker` | Autocomplete search of scene entities by name |
| ComponentReference | `ComponentReferencePicker` | Autocomplete listing of typed components ("Entity → Component") |
| Prefab / AssetReference | `AssetReferencePicker` | Read-only display of asset references |
| List | `ListEditor` | Expandable list with add/remove and inline type-specific editors |
| Dictionary | `DictionaryEditor` | Expandable key-value editor with add/remove |

### Reflection-Based Component Inspection
`ComponentInspector` reflects on `EntityComponent` subclasses to produce `EditableProperty` descriptors:

- Honours `[DataMember(order)]` for property ordering
- Respects `[DataMemberIgnore]` to hide properties
- Uses `[Display("name", "category")]` for friendly names and grouping
- Reads `[DataMemberRange(min, max, step, decimals)]` for numeric constraints
- Skips base-class noise (Entity, Id, Tags, etc.)
- Components implementing `IEditableComponent` can override the reflected property list

### Extensibility
Implement `IEditableComponent` on your Stride components to customise the editor:

```csharp
public class MyComponent : EntityComponent, IEditableComponent
{
    public string DisplayName => "My Custom Component";

    public IReadOnlyList<EditableProperty> GetEditableProperties()
    {
        // Return a custom property list
    }

    public void OnPropertyChanged(string propertyName, object? newValue)
    {
        // Handle property changes with validation
    }
}
```

## Key Classes

| Class | Description |
|-------|-------------|
| `PropertyControlFactory` | Static factory mapping property types to Avalonia controls |
| `ComponentInspector` | Reflection-based property discovery for Stride components |
| `EditableProperty` | Immutable property descriptor with metadata |
| `EditablePropertyType` | Enum of 20 supported property types |
| `IEditableComponent` | Optional interface for components to customise editor behaviour |
| `Vector2Editor` | Two-field colour-coded editor for `Vector2` |
| `Vector3Editor` | Three-field colour-coded editor for `Vector3` |
| `Vector4Editor` | Four-field colour-coded editor for `Vector4` |
| `QuaternionEditor` | Euler angle editor for `Quaternion` |
| `ColorEditor` | HSV colour wheel with hex input |
| `EntityReferencePicker` | Autocomplete entity search |
| `ComponentReferencePicker` | Autocomplete component search |
| `AssetReferencePicker` | Read-only asset reference display |
| `ListEditor` | Expandable inline list editor |
| `DictionaryEditor` | Expandable inline dictionary editor |

## Dependencies

| Package | Version |
|---------|---------|
| Avalonia | 11.3.* |
| FluentAvaloniaUI | 2.* |
| ThemeEditor.Controls.ColorPicker | 11.* |
| Stride.Engine | 4.3.0.2507 |

**Project References:** `Stride.Avalonia`

## Target Framework

`net10.0` (cross-platform)
