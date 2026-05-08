using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BetterNails.src.config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.assets;

internal sealed class Patcher
{
    private readonly ICoreAPI api;
    private readonly Config config;

    public Patcher(ICoreAPI api, Config config)
    {
        this.api = api;
        this.config = config;
    }

    public void Patch()
    {
        List<IAsset> smithingAssets = api.Assets.GetManyInCategory("recipes", "smithing", null, true);
        List<SmithingAsset> nailRecipeAssets = LoadNailRecipeAssets(smithingAssets);

        if (nailRecipeAssets.Count == 0)
        {
            return;
        }

        int baseSetsPerIngot = FindBaseSetsPerIngot(nailRecipeAssets);
        Dictionary<string, JObject> ingotCombustiblePropsByMetal = LoadIngotCombustiblePropsByMetal();
        HashSet<string> producibleIngotMetals = LoadProducibleIngotMetals();
        List<string> discoveredMetals = DiscoverQualifiedNailMetals(nailRecipeAssets, ingotCombustiblePropsByMetal, producibleIngotMetals);

        if (config.AddDiscoveredMetals(discoveredMetals))
        {
            api.StoreModConfig(config, Config.FileName);
        }

        Dictionary<string, int> amountsByMetal = BuildAmountMap(discoveredMetals);

        PatchSmithingRecipes(nailRecipeAssets, amountsByMetal, discoveredMetals, baseSetsPerIngot);
        PatchNailItems(amountsByMetal, ingotCombustiblePropsByMetal);
    }

    private List<SmithingAsset> LoadNailRecipeAssets(IEnumerable<IAsset> smithingAssets)
    {
        List<SmithingAsset> nailRecipeAssets = [];

        foreach (IAsset asset in smithingAssets)
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            List<string> metals = [.. EnumerateRecipeObjects(root)
                .Where(IsNailRecipe)
                .SelectMany(GetRecipeMetals)
                .Select(Names.NormalizeMetal)
                .Where(metal => metal.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            if (metals.Count > 0)
            {
                nailRecipeAssets.Add(new SmithingAsset(asset, root, metals));
            }
        }

        return nailRecipeAssets;
    }

    private List<string> DiscoverQualifiedNailMetals(
        IEnumerable<SmithingAsset> nailRecipeAssets,
        IReadOnlyDictionary<string, JObject> ingotCombustiblePropsByMetal,
        IReadOnlySet<string> producibleIngotMetals
    )
    {
        HashSet<string> qualifiedMetals = new(StringComparer.OrdinalIgnoreCase);

        foreach (string metal in LoadNailItemMetals())
        {
            if (IsQualifiedIngotMetal(metal, ingotCombustiblePropsByMetal, producibleIngotMetals))
            {
                qualifiedMetals.Add(metal);
            }
        }

        foreach (string metal in nailRecipeAssets.SelectMany(asset => asset.Metals))
        {
            string normalizedMetal = Names.NormalizeMetal(metal);

            if (IsQualifiedIngotMetal(normalizedMetal, ingotCombustiblePropsByMetal, producibleIngotMetals))
            {
                qualifiedMetals.Add(normalizedMetal);
            }
        }

        foreach (string metal in LoadAdditionalIngotMetals(ingotCombustiblePropsByMetal.Keys, qualifiedMetals, producibleIngotMetals))
        {
            qualifiedMetals.Add(metal);
        }

        return [.. qualifiedMetals.OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase)];
    }

    private List<string> LoadAdditionalIngotMetals(
        IEnumerable<string> ingotMetals,
        HashSet<string> existingNailMetals,
        IReadOnlySet<string> producibleIngotMetals
    )
    {
        HashSet<string> metalPropertyCodes = LoadMetalPropertyCodes();
        HashSet<string> additionalMetals = new(StringComparer.OrdinalIgnoreCase);

        foreach (string metal in ingotMetals.Select(Names.NormalizeMetal).Where(metal => metal.Length > 0))
        {
            if (existingNailMetals.Contains(metal)
                || !metalPropertyCodes.Contains(metal)
                || !producibleIngotMetals.Contains(metal))
            {
                continue;
            }

            additionalMetals.Add(metal);
        }

        return [.. additionalMetals.OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase)];
    }

    private HashSet<string> LoadProducibleIngotMetals()
    {
        HashSet<string> metals = new(StringComparer.OrdinalIgnoreCase);

        AddRecipeIngotOutputs(metals);
        AddSmeltedIngotOutputs(metals, "resource/nugget");
        AddSmeltedIngotOutputs(metals, "resource/stone");
        AddSmeltedIngotOutputs(metals, "resource/ore");
        AddSmeltedIngotOutputs(metals, "resource/crushed");
        AddCarburizedIngotOutputs(metals);

        return metals;
    }

    private void AddRecipeIngotOutputs(HashSet<string> metals)
    {
        foreach (IAsset asset in api.Assets.GetManyInCategory("recipes", string.Empty, null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            foreach (JObject recipeObject in EnumerateObjectsDeep(root))
            {
                if (!TryGetObject(recipeObject, "output", out JObject output)
                    || !TryGetIngotMetal(output["code"]?.Value<string>(), out string metal))
                {
                    continue;
                }

                metals.Add(metal);
            }
        }
    }

    private void AddSmeltedIngotOutputs(HashSet<string> metals, string itemTypePath)
    {
        foreach (IAsset asset in api.Assets.GetManyInCategory("itemtypes", itemTypePath, null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject itemType || !TryGetObject(itemType, "combustiblePropsByType", out JObject combustiblePropsByType))
            {
                continue;
            }

            List<string> excludedTypePatterns = LoadExcludedTypePatterns(itemType);

            foreach (JProperty combustibleEntry in combustiblePropsByType.Properties())
            {
                if (IsExcludedByType(combustibleEntry.Name, excludedTypePatterns)
                    || combustibleEntry.Value is not JObject combustibleProps
                    || !TryGetIngotMetal(combustibleProps["smeltedStack"]?["code"]?.Value<string>(), out string metal))
                {
                    continue;
                }

                metals.Add(metal);
            }
        }
    }

    private void AddCarburizedIngotOutputs(HashSet<string> metals)
    {
        foreach (IAsset asset in api.Assets.GetManyInCategory("itemtypes", string.Empty, null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject itemType
                || !TryGetObject(itemType, "attributes", out JObject attributes)
                || !TryGetObject(attributes, "carburizablePropsByType", out JObject carburizablePropsByType))
            {
                continue;
            }

            List<string> excludedTypePatterns = LoadExcludedTypePatterns(itemType);

            foreach (JProperty carburizableEntry in carburizablePropsByType.Properties())
            {
                if (IsExcludedByType(carburizableEntry.Name, excludedTypePatterns)
                    || carburizableEntry.Value is not JObject carburizableProps
                    || !TryGetIngotMetal(carburizableProps["carburizedOutput"]?["code"]?.Value<string>(), out string metal))
                {
                    continue;
                }

                metals.Add(metal);
            }
        }
    }

    private HashSet<string> LoadMetalPropertyCodes()
    {
        HashSet<string> metals = new(StringComparer.OrdinalIgnoreCase);

        foreach (IAsset asset in api.Assets.GetManyInCategory("worldproperties", "block/metal", null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject worldProperty || worldProperty["variants"] is not JArray variants)
            {
                continue;
            }

            foreach (JObject variant in variants.OfType<JObject>())
            {
                if (!TryGetString(variant, "code", out string metal))
                {
                    continue;
                }

                string normalizedMetal = Names.NormalizeMetal(metal);

                if (normalizedMetal.Length > 0)
                {
                    metals.Add(normalizedMetal);
                }
            }
        }

        return metals;
    }

    private List<string> LoadNailItemMetals()
    {
        HashSet<string> metals = new(StringComparer.OrdinalIgnoreCase);

        foreach (IAsset asset in api.Assets.GetManyInCategory("itemtypes", "resource/metalnailsandstrips", null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject itemType || itemType["allowedVariants"] is not JArray allowedVariants)
            {
                continue;
            }

            foreach (JToken allowedVariant in allowedVariants)
            {
                string metal = Names.NormalizeMetal(allowedVariant.Value<string>() ?? string.Empty);

                if (metal.Length > 0)
                {
                    metals.Add(metal);
                }
            }
        }

        return [.. metals.OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase)];
    }

    private Dictionary<string, JObject> LoadIngotCombustiblePropsByMetal()
    {
        Dictionary<string, JObject> combustiblePropsByMetal = new(StringComparer.OrdinalIgnoreCase);

        foreach (IAsset asset in api.Assets.GetManyInCategory("itemtypes", "resource/ingot", null, true))
        {
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject ingotType || !TryGetObject(ingotType, "combustiblePropsByType", out JObject combustiblePropsByType))
            {
                continue;
            }

            List<string> excludedTypePatterns = LoadExcludedTypePatterns(ingotType);

            foreach (JProperty combustibleEntry in combustiblePropsByType.Properties())
            {
                string metal = Names.NormalizeMetal(combustibleEntry.Name);

                if (metal.Length == 0
                    || IsExcludedByType(combustibleEntry.Name, excludedTypePatterns)
                    || combustibleEntry.Value is not JObject combustibleProps)
                {
                    continue;
                }

                string? smeltedCode = combustibleProps["smeltedStack"]?["code"]?.Value<string>();

                if (IsIngotForMetal(smeltedCode, metal))
                {
                    combustiblePropsByMetal[metal] = (JObject)combustibleProps.DeepClone();
                }
            }
        }

        return combustiblePropsByMetal;
    }

    private Dictionary<string, int> BuildAmountMap(IEnumerable<string> discoveredMetals)
    {
        Dictionary<string, int> amountsByMetal = new(StringComparer.OrdinalIgnoreCase);

        foreach (string metal in discoveredMetals.Select(Names.NormalizeMetal).Where(metal => metal.Length > 0))
        {
            amountsByMetal[metal] = config.GetAmountForMetal(metal);
        }

        return amountsByMetal;
    }

    private static void PatchSmithingRecipes(
        IEnumerable<SmithingAsset> nailRecipeAssets,
        IReadOnlyDictionary<string, int> amountsByMetal,
        IReadOnlyCollection<string> qualifiedMetals,
        int baseSetsPerIngot
    )
    {
        foreach (SmithingAsset asset in nailRecipeAssets)
        {
            JToken patchedRoot = PatchRecipeRoot(asset.Root, amountsByMetal, qualifiedMetals, baseSetsPerIngot);

            asset.Asset.Data = Encoding.UTF8.GetBytes(patchedRoot.ToString(Formatting.None));
            asset.Asset.IsPatched = true;
        }
    }

    private static JToken PatchRecipeRoot(
        JToken root,
        IReadOnlyDictionary<string, int> amountsByMetal,
        IReadOnlyCollection<string> qualifiedMetals,
        int baseSetsPerIngot
    )
    {
        if (root is JArray recipeArray)
        {
            JArray patchedArray = [];

            foreach (JToken recipeToken in recipeArray)
            {
                foreach (JToken patchedRecipe in PatchRecipeToken(recipeToken, amountsByMetal, qualifiedMetals, baseSetsPerIngot))
                {
                    patchedArray.Add(patchedRecipe);
                }
            }

            return patchedArray;
        }

        if (root is JObject recipeObject)
        {
            List<JToken> patchedRecipes = PatchRecipeToken(recipeObject, amountsByMetal, qualifiedMetals, baseSetsPerIngot);
            return patchedRecipes.Count == 1 ? patchedRecipes[0] : new JArray(patchedRecipes);
        }

        return root;
    }

    private static List<JToken> PatchRecipeToken(
        JToken recipeToken,
        IReadOnlyDictionary<string, int> amountsByMetal,
        IReadOnlyCollection<string> qualifiedMetals,
        int baseSetsPerIngot
    )
    {
        if (recipeToken is not JObject recipe || !IsNailRecipe(recipe))
        {
            return [recipeToken.DeepClone()];
        }

        bool usesMetalPlaceholder = UsesMetalPlaceholder(recipe);
        HashSet<string> qualifiedMetalSet = [.. qualifiedMetals
            .Select(Names.NormalizeMetal)
            .Where(metal => metal.Length > 0)];
        List<string> originalMetals = [.. GetRecipeMetals(recipe)
            .Select(Names.NormalizeMetal)
            .Where(metal => metal.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        List<string> metals = usesMetalPlaceholder
            ? [.. originalMetals
                .Concat(qualifiedMetals)
                .Select(Names.NormalizeMetal)
                .Where(metal => metal.Length > 0)
                .Where(qualifiedMetalSet.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)]
            : [.. originalMetals.Where(qualifiedMetalSet.Contains)];

        if (metals.Count == 0)
        {
            return [];
        }

        int originalStackSize = GetOutputStackSize(recipe);

        if (metals.Count <= 1)
        {
            JObject patchedRecipe = (JObject)recipe.DeepClone();
            string metal = metals.Count == 1 ? metals[0] : string.Empty;
            int amount = CalculateRecipeOutputAmount(amountsByMetal, metal, originalStackSize, baseSetsPerIngot);

            if (usesMetalPlaceholder)
            {
                SetMetalAllowedVariants(patchedRecipe, metals);
            }

            SetOutputStackSize(patchedRecipe, amount);
            return [patchedRecipe];
        }

        var amountGroups = metals
            .GroupBy(metal => CalculateRecipeOutputAmount(amountsByMetal, metal, originalStackSize, baseSetsPerIngot))
            .OrderBy(group => group.Key)
            .ToList();

        if (amountGroups.Count == 1)
        {
            JObject patchedRecipe = (JObject)recipe.DeepClone();
            if (usesMetalPlaceholder)
            {
                SetMetalAllowedVariants(patchedRecipe, metals.OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase));
            }

            SetOutputStackSize(patchedRecipe, amountGroups[0].Key);
            return [patchedRecipe];
        }

        List<JToken> splitRecipes = [];

        foreach (IGrouping<int, string> amountGroup in amountGroups)
        {
            JObject patchedRecipe = (JObject)recipe.DeepClone();
            SetMetalAllowedVariants(patchedRecipe, amountGroup.OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase));
            SetOutputStackSize(patchedRecipe, amountGroup.Key);
            splitRecipes.Add(patchedRecipe);
        }

        return splitRecipes;
    }

    private void PatchNailItems(
        IReadOnlyDictionary<string, int> amountsByMetal,
        IReadOnlyDictionary<string, JObject> ingotCombustiblePropsByMetal
    )
    {
        foreach (IAsset asset in api.Assets.GetManyInCategory("itemtypes", "resource/metalnailsandstrips", null, true))
        {
            bool assetChanged = false;
            JToken root;

            try
            {
                root = JToken.Parse(asset.ToText());
            }
            catch (Exception)
            {
                continue;
            }

            if (root is not JObject itemType)
            {
                continue;
            }

            assetChanged |= PatchNailAllowedVariants(itemType, amountsByMetal.Keys);

            if (!TryGetObject(itemType, "combustiblePropsByType", out JObject combustiblePropsByType))
            {
                combustiblePropsByType = [];
                itemType["combustiblePropsByType"] = combustiblePropsByType;
                assetChanged = true;
            }

            foreach ((string metal, int amount) in amountsByMetal)
            {
                assetChanged |= PatchNailCombustibleProps(combustiblePropsByType, metal, amount, ingotCombustiblePropsByMetal);
            }

            if (assetChanged)
            {
                asset.Data = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
                asset.IsPatched = true;
            }
        }
    }

    private static bool PatchNailCombustibleProps(
        JObject combustiblePropsByType,
        string metal,
        int amount,
        IReadOnlyDictionary<string, JObject> ingotCombustiblePropsByMetal
    )
    {
        string itemCode = Names.NailOutputPrefix + metal;

        if (!TryGetObject(combustiblePropsByType, itemCode, out JObject combustibleProps))
        {
            if (!ingotCombustiblePropsByMetal.TryGetValue(metal, out JObject? ingotCombustibleProps))
            {
                return false;
            }

            combustiblePropsByType[itemCode] = CreateNailCombustibleProps(metal, amount, ingotCombustibleProps);
            return true;
        }

        string? smeltedCode = combustibleProps["smeltedStack"]?["code"]?.Value<string>();

        if (!IsIngotForMetal(smeltedCode, metal))
        {
            if (!ingotCombustiblePropsByMetal.TryGetValue(metal, out JObject? ingotCombustibleProps))
            {
                return false;
            }

            combustiblePropsByType[itemCode] = CreateNailCombustibleProps(metal, amount, ingotCombustibleProps);
            return true;
        }

        combustibleProps["smeltedRatio"] = amount;
        return true;
    }

    private static bool PatchNailAllowedVariants(JObject itemType, IEnumerable<string> metals)
    {
        if (itemType["allowedVariants"] is not JArray allowedVariants)
        {
            allowedVariants = [];
            itemType["allowedVariants"] = allowedVariants;
        }

        HashSet<string> existingMetals = [.. allowedVariants
            .Select(variant => Names.NormalizeMetal(variant.Value<string>() ?? string.Empty))
            .Where(metal => metal.Length > 0)];
        bool changed = false;

        foreach (string metal in metals.Select(Names.NormalizeMetal).Where(metal => metal.Length > 0).OrderBy(metal => metal, StringComparer.OrdinalIgnoreCase))
        {
            if (!existingMetals.Add(metal))
            {
                continue;
            }

            allowedVariants.Add("*-" + metal);
            changed = true;
        }

        return changed;
    }

    private static JObject CreateNailCombustibleProps(string metal, int amount, JObject ingotCombustibleProps)
    {
        JObject combustibleProps = (JObject)ingotCombustibleProps.DeepClone();

        combustibleProps["smeltedRatio"] = amount;
        combustibleProps["smeltedStack"] = new JObject
        {
            ["type"] = "item",
            ["code"] = "ingot-" + metal,
            ["stacksize"] = 1
        };

        return combustibleProps;
    }

    private static IEnumerable<JObject> EnumerateRecipeObjects(JToken root)
    {
        if (root is JArray recipeArray)
        {
            return recipeArray.OfType<JObject>();
        }

        return root is JObject recipeObject ? [recipeObject] : Enumerable.Empty<JObject>();
    }

    private static IEnumerable<JObject> EnumerateObjectsDeep(JToken root)
    {
        if (root is JObject rootObject)
        {
            yield return rootObject;
        }

        if (root is not JContainer container)
        {
            yield break;
        }

        foreach (JObject childObject in container.Descendants().OfType<JObject>())
        {
            yield return childObject;
        }
    }

    private static bool IsNailRecipe(JObject recipe)
    {
        string? outputCode = recipe["output"]?["code"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(outputCode))
        {
            return false;
        }

        string normalizedCode = outputCode.ToLowerInvariant();
        return normalizedCode == Names.NailOutputCode
            || normalizedCode.StartsWith(Names.NailOutputPrefix, StringComparison.Ordinal);
    }

    private static bool UsesMetalPlaceholder(JObject recipe)
    {
        string? outputCode = recipe["output"]?["code"]?.Value<string>();
        return outputCode?.Contains(Names.MetalPlaceholder, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<string> GetRecipeMetals(JObject recipe)
    {
        string? outputCode = recipe["output"]?["code"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(outputCode))
        {
            yield break;
        }

        if (!outputCode.Contains(Names.MetalPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            string normalizedCode = outputCode.ToLowerInvariant();

            if (normalizedCode.StartsWith(Names.NailOutputPrefix, StringComparison.Ordinal))
            {
                yield return normalizedCode[Names.NailOutputPrefix.Length..];
            }

            yield break;
        }

        JObject? metalIngredient = FindMetalIngredient(recipe);

        if (metalIngredient?["allowedVariants"] is not JArray allowedVariants)
        {
            yield break;
        }

        foreach (JToken variant in allowedVariants)
        {
            string? metal = variant.Value<string>();

            if (!string.IsNullOrWhiteSpace(metal))
            {
                yield return metal;
            }
        }
    }

    private static JObject? FindMetalIngredient(JObject recipe)
    {
        if (recipe["ingredient"] is JObject ingredient && IsMetalVariantSource(ingredient))
        {
            return ingredient;
        }

        return recipe
            .Descendants()
            .OfType<JObject>()
            .FirstOrDefault(IsMetalVariantSource);
    }

    private static bool IsMetalVariantSource(JObject value)
    {
        return value["allowedVariants"] is JArray
            && string.Equals(value["name"]?.Value<string>(), "metal", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetOutputStackSize(JObject recipe)
    {
        JObject? output = recipe["output"] as JObject;

        if (output == null)
        {
            return 1;
        }

        return output["stacksize"]?.Value<int?>()
            ?? output["quantity"]?.Value<int?>()
            ?? 1;
    }

    private static void SetOutputStackSize(JObject recipe, int stackSize)
    {
        if (recipe["output"] is not JObject output)
        {
            return;
        }

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

    private static void SetMetalAllowedVariants(JObject recipe, IEnumerable<string> metals)
    {
        JObject? metalIngredient = FindMetalIngredient(recipe);

        if (metalIngredient != null)
        {
            metalIngredient["allowedVariants"] = new JArray(metals);
        }
    }

    private static int FindBaseSetsPerIngot(IEnumerable<SmithingAsset> nailRecipeAssets)
    {
        int baseSetsPerIngot = nailRecipeAssets
            .SelectMany(asset => EnumerateRecipeObjects(asset.Root))
            .Where(IsNailRecipe)
            .Select(GetOutputStackSize)
            .Where(stackSize => stackSize > 0)
            .DefaultIfEmpty(Config.VanillaSetsPerIngot)
            .Min();

        return baseSetsPerIngot > 0 ? baseSetsPerIngot : Config.VanillaSetsPerIngot;
    }

    private static int CalculateRecipeOutputAmount(
        IReadOnlyDictionary<string, int> amountsByMetal,
        string metal,
        int originalStackSize,
        int baseSetsPerIngot
    )
    {
        int configuredAmount = amountsByMetal.TryGetValue(metal, out int amount)
            ? amount
            : Config.VanillaSetsPerIngot;
        decimal ingotMultiplier = baseSetsPerIngot > 0 ? originalStackSize / (decimal)baseSetsPerIngot : 1m;

        return Math.Max(1, (int)Math.Round(configuredAmount * ingotMultiplier, MidpointRounding.AwayFromZero));
    }

    private static bool TryGetObject(JObject parent, string propertyName, out JObject value)
    {
        JProperty? property = parent
            .Properties()
            .FirstOrDefault(prop => string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        value = property?.Value as JObject ?? [];
        return property?.Value is JObject;
    }

    private static bool TryGetString(JObject parent, string propertyName, out string value)
    {
        JProperty? property = parent
            .Properties()
            .FirstOrDefault(prop => string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        value = property?.Value.Value<string>() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsQualifiedIngotMetal(
        string metal,
        IReadOnlyDictionary<string, JObject> ingotCombustiblePropsByMetal,
        IReadOnlySet<string> producibleIngotMetals
    )
    {
        string normalizedMetal = Names.NormalizeMetal(metal);

        return normalizedMetal.Length > 0
            && ingotCombustiblePropsByMetal.ContainsKey(normalizedMetal)
            && producibleIngotMetals.Contains(normalizedMetal);
    }

    private static List<string> LoadExcludedTypePatterns(JObject itemType)
    {
        List<string> patterns = [];

        if (!TryGetObject(itemType, "attributes", out JObject attributes)
            || !TryGetObject(attributes, "handbook", out JObject handbook)
            || !TryGetObject(handbook, "excludeByType", out JObject excludeByType))
        {
            return patterns;
        }

        foreach (JProperty excludeEntry in excludeByType.Properties())
        {
            if (excludeEntry.Value.Value<bool?>() == false)
            {
                continue;
            }

            patterns.Add(excludeEntry.Name.Trim().ToLowerInvariant());
        }

        return patterns;
    }

    private static bool IsExcludedByType(string codePattern, IEnumerable<string> excludedTypePatterns)
    {
        string normalizedCodePattern = codePattern.Trim().ToLowerInvariant();
        return excludedTypePatterns.Any(excludedPattern => TypePatternMatches(normalizedCodePattern, excludedPattern));
    }

    private static bool TypePatternMatches(string value, string pattern)
    {
        if (string.Equals(value, pattern, StringComparison.Ordinal))
        {
            return true;
        }

        if (WildcardMatches(value, pattern))
        {
            return true;
        }

        HashSet<string> valueParts = [.. value.Split(['*', '-'], StringSplitOptions.RemoveEmptyEntries)];
        string[] patternParts = pattern.Split(['*', '-'], StringSplitOptions.RemoveEmptyEntries);

        return patternParts.Length > 0 && patternParts.All(valueParts.Contains);
    }

    private static bool WildcardMatches(string value, string pattern)
    {
        string[] segments = pattern.Split('*');
        int position = 0;

        foreach (string segment in segments.Where(segment => segment.Length > 0))
        {
            int nextPosition = value.IndexOf(segment, position, StringComparison.Ordinal);

            if (nextPosition < 0)
            {
                return false;
            }

            position = nextPosition + segment.Length;
        }

        string firstSegment = segments.FirstOrDefault(segment => segment.Length > 0) ?? string.Empty;
        string lastSegment = segments.LastOrDefault(segment => segment.Length > 0) ?? string.Empty;

        return (pattern.StartsWith('*') || firstSegment.Length == 0 || value.StartsWith(firstSegment, StringComparison.Ordinal))
            && (pattern.EndsWith('*') || lastSegment.Length == 0 || value.EndsWith(lastSegment, StringComparison.Ordinal));
    }

    private static bool TryGetIngotMetal(string? itemCode, out string metal)
    {
        metal = string.Empty;

        if (string.IsNullOrWhiteSpace(itemCode))
        {
            return false;
        }

        string normalizedCode = itemCode.Trim().ToLowerInvariant();
        int domainSeparatorIndex = normalizedCode.LastIndexOf(':');

        if (domainSeparatorIndex >= 0)
        {
            normalizedCode = normalizedCode[(domainSeparatorIndex + 1)..];
        }

        if (!normalizedCode.StartsWith("ingot-", StringComparison.Ordinal)
            || normalizedCode.Contains('{', StringComparison.Ordinal)
            || normalizedCode.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        metal = Names.NormalizeMetal(normalizedCode);
        return metal.Length > 0;
    }

    private static bool IsIngotForMetal(string? itemCode, string metal)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            return false;
        }

        string expectedCode = "ingot-" + metal;
        string normalizedCode = itemCode.Trim().ToLowerInvariant();

        return normalizedCode == expectedCode || normalizedCode.EndsWith(":" + expectedCode, StringComparison.Ordinal);
    }

    private sealed class SmithingAsset(IAsset asset, JToken root, List<string> metals)
    {
        public IAsset Asset { get; } = asset;

        public JToken Root { get; } = root;

        public List<string> Metals { get; } = metals;
    }
}
