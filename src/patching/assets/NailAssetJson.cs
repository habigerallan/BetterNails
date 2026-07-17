using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.assets;

public static class NailAssetJson
{
    public static bool TryParse(IAsset asset, out JToken root)
    {
        try
        {
            root = JToken.Parse(asset.ToText());
            return true;
        }
        catch (Exception)
        {
            root = new JObject();
            return false;
        }
    }

    public static void Write(IAsset asset, JToken root)
    {
        asset.Data = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
        asset.IsPatched = true;
    }

    public static List<JObject> GetRecipeObjects(JToken root)
    {
        List<JObject> recipeObjects = [];

        if (root is JArray recipeArray)
        {
            foreach (JToken recipeToken in recipeArray)
            {
                if (recipeToken is JObject recipeObject)
                {
                    recipeObjects.Add(recipeObject);
                }
            }

            return recipeObjects;
        }

        if (root is JObject rootObject)
        {
            recipeObjects.Add(rootObject);
        }

        return recipeObjects;
    }

    public static List<JObject> GetObjectsDeep(JToken root)
    {
        List<JObject> objects = [];

        if (root is JObject rootObject)
        {
            objects.Add(rootObject);
        }

        if (root is not JContainer container) return objects;

        foreach (JToken descendant in container.Descendants())
        {
            if (descendant is JObject descendantObject)
            {
                objects.Add(descendantObject);
            }
        }

        return objects;
    }

    public static bool TryGetObject(JObject parent, string propertyName, out JObject value)
    {
        foreach (JProperty property in parent.Properties())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            if (property.Value is JObject objectValue)
            {
                value = objectValue;
                return true;
            }

            break;
        }

        value = new JObject();
        return false;
    }

    public static bool TryGetString(JObject parent, string propertyName, out string value)
    {
        foreach (JProperty property in parent.Properties())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            value = GetStringValue(property.Value);
            return string.IsNullOrWhiteSpace(value) == false;
        }

        value = string.Empty;
        return false;
    }

    public static string GetStringValue(JToken value)
    {
        if (value == null) return string.Empty;

        return value.Value<string>() ?? string.Empty;
    }

    public static int GetOutputStackSize(JObject recipe)
    {
        if (recipe["output"] is not JObject output) return 1;

        JToken stackSize = output["stacksize"];

        if (HasValue(stackSize)) return stackSize.Value<int>();

        JToken quantity = output["quantity"];

        if (HasValue(quantity)) return quantity.Value<int>();

        return 1;
    }

    public static void SetOutputStackSize(JObject recipe, int stackSize)
    {
        if (recipe["output"] is not JObject output) return;

        if (output.Property("stacksize") != null)
        {
            output["stacksize"] = stackSize;
            return;
        }

        if (output.Property("quantity") != null)
        {
            output["quantity"] = stackSize;
            return;
        }

        output["stacksize"] = stackSize;
    }

    public static List<string> LoadExcludedTypePatterns(JObject itemType)
    {
        List<string> patterns = [];

        if (TryGetObject(itemType, "attributes", out JObject attributes) == false
            || TryGetObject(attributes, "handbook", out JObject handbook) == false
            || TryGetObject(handbook, "excludeByType", out JObject excludeByType) == false)
        {
            return patterns;
        }

        foreach (JProperty excludeEntry in excludeByType.Properties())
        {
            if (IsFalseValue(excludeEntry.Value)) continue;

            patterns.Add(excludeEntry.Name.Trim().ToLowerInvariant());
        }

        return patterns;
    }

    public static bool IsExcludedByType(string codePattern, List<string> excludedTypePatterns)
    {
        string normalizedCodePattern = codePattern.Trim().ToLowerInvariant();

        foreach (string excludedPattern in excludedTypePatterns)
        {
            if (TypePatternMatches(normalizedCodePattern, excludedPattern)) return true;
        }

        return false;
    }

    private static bool HasValue(JToken value)
    {
        if (value == null) return false;

        return value.Type != JTokenType.Null && value.Type != JTokenType.Undefined;
    }

    private static bool IsFalseValue(JToken value)
    {
        if (HasValue(value) == false) return false;

        return value.Value<bool>() == false;
    }

    private static bool TypePatternMatches(string value, string pattern)
    {
        if (string.Equals(value, pattern, StringComparison.Ordinal)) return true;
        if (WildcardMatches(value, pattern)) return true;

        HashSet<string> valueParts = new(value.Split(
            ['*', '-'],
            StringSplitOptions.RemoveEmptyEntries
        ));
        string[] patternParts = pattern.Split(['*', '-'], StringSplitOptions.RemoveEmptyEntries);

        if (patternParts.Length == 0) return false;

        foreach (string patternPart in patternParts)
        {
            if (valueParts.Contains(patternPart) == false) return false;
        }

        return true;
    }

    private static bool WildcardMatches(string value, string pattern)
    {
        string[] segments = pattern.Split('*');
        int position = 0;
        string firstSegment = string.Empty;
        string lastSegment = string.Empty;

        foreach (string segment in segments)
        {
            if (segment.Length == 0) continue;

            if (firstSegment.Length == 0) firstSegment = segment;

            lastSegment = segment;
            int nextPosition = value.IndexOf(segment, position, StringComparison.Ordinal);

            if (nextPosition < 0) return false;

            position = nextPosition + segment.Length;
        }

        bool startsAtBeginning = pattern.StartsWith('*')
            || firstSegment.Length == 0
            || value.StartsWith(firstSegment, StringComparison.Ordinal);
        bool endsAtEnd = pattern.EndsWith('*')
            || lastSegment.Length == 0
            || value.EndsWith(lastSegment, StringComparison.Ordinal);

        return startsAtBeginning && endsAtEnd;
    }
}
