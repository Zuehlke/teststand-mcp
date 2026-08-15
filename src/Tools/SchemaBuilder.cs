using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestStandMCP.Tools;

/// <summary>Builds JSON Schema objects for MCP tool input schemas.</summary>
public static class SchemaBuilder
{
    /// <summary>
    /// Property names a tool must never declare. The MCP client parses <c>tools/list</c> in
    /// JavaScript and validates <c>inputSchema.properties</c> as a RECORD; an own property
    /// with one of these names makes the object fail that check ("expected record, received
    /// object"). The client then discards the ENTIRE tool list — 252 tools disappear because
    /// of one parameter name. Measured 2026-08-15: <c>configure_dotnet_module</c> gained a
    /// parameter literally named <c>constructor</c> and took the whole server offline.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedPropertyNames = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "constructor", "__proto__", "prototype", "toString", "toLocaleString",
        "valueOf", "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable"
    };

    /// <summary>
    /// Throws when a property name would break the client's schema validation. Called on every
    /// property as it is declared, so the failure happens at server start (a loud, immediate
    /// crash) instead of silently emptying the tool catalogue of a running client.
    /// </summary>
    public static void GuardPropertyName(string name)
    {
        if (ReservedPropertyNames.Contains(name))
            throw new ArgumentException(
                $"Tool parameter '{name}' is a JavaScript prototype key. A schema property with " +
                "that name makes the MCP client reject the whole tools/list response, so every " +
                "tool of this server disappears. Rename it (e.g. 'constructor_signature').",
                nameof(name));
    }

    // Cached once: a JsonSerializerOptions instance is expensive to build and caches
    // serialization metadata internally — allocating a fresh one per call defeats that.
    private static readonly JsonSerializerOptions _schemaOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds a JSON Schema <see cref="JsonElement"/> from a fluent
    /// <see cref="SchemaObject"/> configured by <paramref name="configure"/>.
    /// </summary>
    public static JsonElement Build(Action<SchemaObject> configure)
    {
        var schema = new SchemaObject { Type = "object" };
        configure(schema);
        // An object schema always carries a "properties" key — a no-argument tool gets an
        // empty one rather than omitting it. Strict validators treat the missing key as a
        // malformed schema, and it is the whole tool that gets rejected, not the key.
        schema.Properties ??= new();
        var json = JsonSerializer.Serialize(schema, _schemaOpts);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

/// <summary>Fluent builder for a JSON Schema object node.</summary>
public class SchemaObject
{
    /// <summary>JSON Schema type; "object" for a schema root.</summary>
    public string Type { get; set; } = "object";
    /// <summary>Named properties of the object, or null when none.</summary>
    public Dictionary<string, SchemaProperty>? Properties { get; set; }
    /// <summary>Names of required properties, or null when none.</summary>
    public List<string>? Required { get; set; }
    /// <summary>Optional description of the object.</summary>
    public string? Description { get; set; }

    /// <summary>Adds a required scalar property with an optional enum constraint.</summary>
    public SchemaObject AddRequired(string name, string type, string description,
        string[]? enumValues = null, string itemType = "string")
    {
        Properties ??= new();
        Required    ??= new();
        SchemaBuilder.GuardPropertyName(name);
        Properties[name] = new SchemaProperty
        {
            Type        = type,
            Description = description,
            Enum        = enumValues,
            Items       = ItemsFor(type, itemType)
        };
        Required.Add(name);
        return this;
    }

    /// <summary>Adds an optional scalar property with an optional default and enum constraint.</summary>
    public SchemaObject AddOptional(string name, string type, string description,
        object? defaultValue = null, string[]? enumValues = null, string itemType = "string")
    {
        Properties ??= new();
        SchemaBuilder.GuardPropertyName(name);
        Properties[name] = new SchemaProperty
        {
            Type        = type,
            Description = description,
            Default     = defaultValue,
            Enum        = enumValues,
            Items       = ItemsFor(type, itemType)
        };
        return this;
    }

    /// <summary>
    /// An array property needs an "items" schema — without one the property is incomplete
    /// and a strict validator rejects the tool. Every array declared through
    /// <see cref="AddRequired"/>/<see cref="AddOptional"/> is a list of scalars (arrays of
    /// objects go through <see cref="AddArray"/>, which builds its own item schema).
    /// </summary>
    private static SchemaProperty? ItemsFor(string type, string itemType) =>
        type == "array" ? new SchemaProperty { Type = itemType } : null;

    /// <summary>
    /// Adds an array property whose items are objects described by <paramref name="itemConfigure"/>.
    /// The item object's required-field list is emitted on the item schema.
    /// </summary>
    public SchemaObject AddArray(string name, string description,
        Action<SchemaObject> itemConfigure, bool required = true)
    {
        Properties ??= new();
        var item = new SchemaObject { Type = "object" };
        itemConfigure(item);
        SchemaBuilder.GuardPropertyName(name);
        Properties[name] = new SchemaProperty
        {
            Type        = "array",
            Description = description,
            Items       = new SchemaProperty
            {
                Type       = "object",
                Properties = item.Properties ?? new(),
                Required   = item.Required
            }
        };
        if (required)
        {
            Required ??= new();
            Required.Add(name);
        }
        return this;
    }

    /// <summary>Adds a nested object property described by <paramref name="configure"/>.</summary>
    public SchemaObject AddObjectProperty(string name, string description,
        Action<SchemaObject> configure)
    {
        Properties ??= new();
        var nested = new SchemaObject { Type = "object", Description = description };
        configure(nested);
        SchemaBuilder.GuardPropertyName(name);
        Properties[name] = new SchemaProperty
        {
            Type        = "object",
            Description = description,
            Properties  = nested.Properties ?? new()
        };
        return this;
    }
}

/// <summary>A single JSON Schema property node.</summary>
public class SchemaProperty
{
    /// <summary>JSON Schema type (e.g. "string", "number", "array", "object").</summary>
    public string Type { get; set; } = "string";
    /// <summary>Optional property description.</summary>
    public string? Description { get; set; }
    /// <summary>Optional default value.</summary>
    public object? Default { get; set; }
    /// <summary>Optional set of allowed values.</summary>
    public string[]? Enum { get; set; }
    /// <summary>Nested properties for object-typed schemas.</summary>
    public Dictionary<string, SchemaProperty>? Properties { get; set; }
    /// <summary>Required nested property names for object-typed schemas.</summary>
    public List<string>? Required { get; set; }
    /// <summary>Item schema for array-typed schemas.</summary>
    public SchemaProperty? Items { get; set; }
}
