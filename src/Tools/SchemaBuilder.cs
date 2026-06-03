using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TestStandMCP.Tools;

/// <summary>Builds JSON Schema objects for MCP tool input schemas.</summary>
public static class SchemaBuilder
{
    public static JsonElement Build(Action<SchemaObject> configure)
    {
        var schema = new SchemaObject { Type = "object" };
        configure(schema);
        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            PropertyNamingPolicy       = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition     = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

public class SchemaObject
{
    public string Type { get; set; } = "object";
    public Dictionary<string, SchemaProperty>? Properties { get; set; }
    public List<string>? Required { get; set; }
    public string? Description { get; set; }

    public SchemaObject AddRequired(string name, string type, string description,
        string[]? enumValues = null)
    {
        Properties ??= new();
        Required    ??= new();
        Properties[name] = new SchemaProperty { Type = type, Description = description, Enum = enumValues };
        Required.Add(name);
        return this;
    }

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

public class SchemaProperty
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public object? Default { get; set; }
    public string[]? Enum { get; set; }
    public Dictionary<string, SchemaProperty>? Properties { get; set; }
    public List<string>? Required { get; set; }
    public SchemaProperty? Items { get; set; }
}
