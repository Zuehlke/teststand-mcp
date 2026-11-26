using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TestStandMCP.Models;

namespace TestStandMCP.Tools;

/// <summary>Leaf-level before/after comparison of two property-tree snapshots (run_step_substep).</summary>
internal static class PropertyTreeDiff
{
    /// <summary>Flattens a tree to dotted leaf path -> value; arrays use "[i]". A node without a value
    /// and without children (an empty array or container) is structure, not a leaf: it is skipped, so an
    /// array that gets its first element shows only the new element's leaves, not a "null -> null" row.</summary>
    public static Dictionary<string, object?> Flatten(PropertyNode root)
    {
        var leaves = new Dictionary<string, object?>();
        void Walk(PropertyNode n, string path)
        {
            if (n.Children == null || n.Children.Count == 0)
            {
                if (path.Length > 0 && n.Value != null) leaves[path] = n.Value;
                return;
            }
            foreach (var c in n.Children)
                Walk(c, c.Name.StartsWith("[") || path.Length == 0 ? path + c.Name : path + "." + c.Name);
        }
        Walk(root, "");
        return leaves;
    }

    /// <summary>Leaves added, removed or changed, ordered by path. Values compare by their JSON form,
    /// so an enum's {ordinal, symbolicName} object compares by content.</summary>
    public static List<PropertyChange> Diff(Dictionary<string, object?> before, Dictionary<string, object?> after)
    {
        var changes = new List<PropertyChange>();
        foreach (var path in before.Keys.Union(after.Keys).OrderBy(p => p, System.StringComparer.Ordinal))
        {
            before.TryGetValue(path, out var b);
            after.TryGetValue(path, out var a);
            if (!before.ContainsKey(path) || !after.ContainsKey(path) ||
                JsonSerializer.Serialize(b) != JsonSerializer.Serialize(a))
                changes.Add(new PropertyChange { Path = path, Before = b, After = a });
        }
        return changes;
    }
}
