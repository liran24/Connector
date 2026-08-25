using System.Text.Json;
using AngleSharp.Html.Parser;

namespace AiVisibility.Core.StructuredData;

/// <summary>
/// Pulls schema.org JSON-LD objects out of a page the way a crawler does: every
/// <c>&lt;script type="application/ld+json"&gt;</c> block, flattened through
/// <c>@graph</c> containers and arrays.
/// </summary>
public static class JsonLdExtractor
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Returns every JSON-LD node on the page. Malformed blocks are skipped rather than
    /// throwing — a store with one broken script still has a report to produce.
    /// </summary>
    public static IReadOnlyList<JsonElement> Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<JsonElement>();
        }

        var document = Parser.ParseDocument(html);
        var nodes = new List<JsonElement>();

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var payload = script.TextContent;
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                continue;
            }

            Flatten(parsed.RootElement.Clone(), nodes);
        }

        return nodes;
    }

    /// <summary>Finds the first node whose <c>@type</c> includes <paramref name="type"/>.</summary>
    public static JsonElement? FindByType(IEnumerable<JsonElement> nodes, string type)
    {
        foreach (var node in nodes)
        {
            if (HasType(node, type))
            {
                return node;
            }
        }

        return null;
    }

    public static bool HasType(JsonElement node, string type)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("@type", out var typeNode))
        {
            return false;
        }

        return typeNode.ValueKind switch
        {
            JsonValueKind.String => Equals(typeNode.GetString(), type),
            JsonValueKind.Array => typeNode.EnumerateArray()
                .Any(t => t.ValueKind == JsonValueKind.String && Equals(t.GetString(), type)),
            _ => false
        };
    }

    private static bool Equals(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Expands arrays and <c>@graph</c> wrappers so callers see a flat list of typed nodes.
    /// </summary>
    private static void Flatten(JsonElement element, List<JsonElement> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, into);
                }

                return;

            case JsonValueKind.Object:
                if (element.TryGetProperty("@graph", out var graph))
                {
                    Flatten(graph, into);
                }

                into.Add(element);
                return;
        }
    }
}
