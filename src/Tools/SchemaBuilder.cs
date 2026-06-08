using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestStandMCP.Tools;

/// <summary>Builds JSON Schema objects for MCP tool input schemas.</summary>
public static class SchemaBuilder
{
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
        string[]? enumValues = null)
    {
        Properties ??= new();
        Required    ??= new();
        Properties[name] = new SchemaProperty { Type = type, Description = description, Enum = enumValues };
        Required.Add(name);
        return this;
    }

    /// <summary>Adds an optional scalar property with an optional default and enum constraint.</summary>
    public SchemaObject AddOptional(string name, string type, string description,
        object? defaultValue = null, string[]? enumValues = null)
    {
        Properties ??= new();
        Properties[name] = new SchemaProperty
        {
            Type        = type,
            Description = description,
            Default     = defaultValue,
            Enum        = enumValues
        };
        return this;
    }

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
        Properties[name] = new SchemaProperty
        {
            Type        = "array",
            Description = description,
            Items       = new SchemaProperty
            {
                Type       = "object",
                Properties = item.Properties,
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
        Properties[name] = new SchemaProperty
        {
            Type        = "object",
            Description = description,
            Properties  = nested.Properties
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
