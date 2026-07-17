using System;
using System.Collections.Generic;
using BetterNails.src.patching.models;
using BetterNails.src.patching.rules;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.assets;

public sealed class NailMetalDiscovery(ICoreAPI api)
{
    private readonly ICoreAPI _api = api;

    public NailMetalCatalog Discover(List<NailRecipeAsset> nailRecipeAssets)
    {
        Dictionary<string, JObject> ingotCombustiblePropsByMetal =
            LoadIngotCombustiblePropsByMetal();
        HashSet<string> producibleIngotMetals = LoadProducibleIngotMetals();
        List<string> discoveredMetals = DiscoverQualifiedNailMetals(
            nailRecipeAssets,
            ingotCombustiblePropsByMetal,
            producibleIngotMetals
        );

        return new NailMetalCatalog(discoveredMetals, ingotCombustiblePropsByMetal);
    }

    private List<string> DiscoverQualifiedNailMetals(
        List<NailRecipeAsset> nailRecipeAssets,
        Dictionary<string, JObject> ingotCombustiblePropsByMetal,
        HashSet<string> producibleIngotMetals
    )
    {
        HashSet<string> qualifiedMetals = new(StringComparer.OrdinalIgnoreCase);
        List<string> nailItemMetals = LoadNailItemMetals();

        foreach (string metal in nailItemMetals)
        {
            if (IsQualifiedIngotMetal(
                    metal,
                    ingotCombustiblePropsByMetal,
                    producibleIngotMetals
                ))
            {
                qualifiedMetals.Add(metal);
            }
        }

        foreach (NailRecipeAsset nailRecipeAsset in nailRecipeAssets)
        {
            foreach (string metal in nailRecipeAsset.Metals)
            {
                string normalizedMetal = NailCode.NormalizeMetal(metal);

                if (IsQualifiedIngotMetal(
                        normalizedMetal,
                        ingotCombustiblePropsByMetal,
                        producibleIngotMetals
                    ))
                {
                    qualifiedMetals.Add(normalizedMetal);
                }
            }
        }

        List<string> additionalMetals = LoadAdditionalIngotMetals(
            ingotCombustiblePropsByMetal,
            qualifiedMetals,
            producibleIngotMetals
        );

        foreach (string metal in additionalMetals)
        {
            qualifiedMetals.Add(metal);
        }

        List<string> sortedMetals = new(qualifiedMetals);
        sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);
        return sortedMetals;
    }

    private List<string> LoadAdditionalIngotMetals(
        Dictionary<string, JObject> ingotCombustiblePropsByMetal,
        HashSet<string> existingNailMetals,
        HashSet<string> producibleIngotMetals
    )
    {
        HashSet<string> metalPropertyCodes = LoadMetalPropertyCodes();
        HashSet<string> additionalMetals = new(StringComparer.OrdinalIgnoreCase);

        // additional variants need a metal property and a real production path
        foreach (string ingotMetal in ingotCombustiblePropsByMetal.Keys)
        {
            string metal = NailCode.NormalizeMetal(ingotMetal);

            if (metal.Length == 0) continue;
            if (existingNailMetals.Contains(metal)) continue;
            if (metalPropertyCodes.Contains(metal) == false) continue;
            if (producibleIngotMetals.Contains(metal) == false) continue;

            additionalMetals.Add(metal);
        }

        List<string> sortedMetals = new(additionalMetals);
        sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);
        return sortedMetals;
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
        List<IAsset> assets = _api.Assets.GetManyInCategory("recipes", string.Empty, null, true);

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            List<JObject> recipeObjects = NailAssetJson.GetObjectsDeep(root);

            foreach (JObject recipeObject in recipeObjects)
            {
                if (NailAssetJson.TryGetObject(recipeObject, "output", out JObject output) == false)
                {
                    continue;
                }

                string outputCode = NailAssetJson.GetStringValue(output["code"]);

                if (NailCode.TryGetIngotMetal(outputCode, out string metal) == false) continue;

                metals.Add(metal);
            }
        }
    }

    private void AddSmeltedIngotOutputs(HashSet<string> metals, string itemTypePath)
    {
        List<IAsset> assets = _api.Assets.GetManyInCategory(
            "itemtypes",
            itemTypePath,
            null,
            true
        );

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            if (root is not JObject itemType
                || NailAssetJson.TryGetObject(
                    itemType,
                    "combustiblePropsByType",
                    out JObject combustiblePropsByType
                ) == false)
            {
                continue;
            }

            List<string> excludedTypePatterns = NailAssetJson.LoadExcludedTypePatterns(itemType);

            foreach (JProperty combustibleEntry in combustiblePropsByType.Properties())
            {
                if (NailAssetJson.IsExcludedByType(
                        combustibleEntry.Name,
                        excludedTypePatterns
                    ))
                {
                    continue;
                }

                if (combustibleEntry.Value is not JObject combustibleProps) continue;

                string smeltedCode = GetStackCode(combustibleProps, "smeltedStack");

                if (NailCode.TryGetIngotMetal(smeltedCode, out string metal) == false) continue;

                metals.Add(metal);
            }
        }
    }

    private void AddCarburizedIngotOutputs(HashSet<string> metals)
    {
        List<IAsset> assets = _api.Assets.GetManyInCategory("itemtypes", string.Empty, null, true);

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            if (root is not JObject itemType
                || NailAssetJson.TryGetObject(itemType, "attributes", out JObject attributes) == false
                || NailAssetJson.TryGetObject(
                    attributes,
                    "carburizablePropsByType",
                    out JObject carburizablePropsByType
                ) == false)
            {
                continue;
            }

            List<string> excludedTypePatterns = NailAssetJson.LoadExcludedTypePatterns(itemType);

            foreach (JProperty carburizableEntry in carburizablePropsByType.Properties())
            {
                if (NailAssetJson.IsExcludedByType(
                        carburizableEntry.Name,
                        excludedTypePatterns
                    ))
                {
                    continue;
                }

                if (carburizableEntry.Value is not JObject carburizableProps) continue;

                string carburizedCode = GetStackCode(carburizableProps, "carburizedOutput");

                if (NailCode.TryGetIngotMetal(carburizedCode, out string metal) == false) continue;

                metals.Add(metal);
            }
        }
    }

    private HashSet<string> LoadMetalPropertyCodes()
    {
        HashSet<string> metals = new(StringComparer.OrdinalIgnoreCase);
        List<IAsset> assets = _api.Assets.GetManyInCategory(
            "worldproperties",
            "block/metal",
            null,
            true
        );

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            if (root is not JObject worldProperty
                || worldProperty["variants"] is not JArray variants)
            {
                continue;
            }

            foreach (JToken variantToken in variants)
            {
                if (variantToken is not JObject variant) continue;
                if (NailAssetJson.TryGetString(variant, "code", out string metal) == false) continue;

                string normalizedMetal = NailCode.NormalizeMetal(metal);

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
        List<IAsset> assets = _api.Assets.GetManyInCategory(
            "itemtypes",
            "resource/metalnailsandstrips",
            null,
            true
        );

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            if (root is not JObject itemType
                || itemType["allowedVariants"] is not JArray allowedVariants)
            {
                continue;
            }

            foreach (JToken allowedVariant in allowedVariants)
            {
                string metal = NailCode.NormalizeMetal(
                    NailAssetJson.GetStringValue(allowedVariant)
                );

                if (metal.Length > 0)
                {
                    metals.Add(metal);
                }
            }
        }

        List<string> sortedMetals = new(metals);
        sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);
        return sortedMetals;
    }

    private Dictionary<string, JObject> LoadIngotCombustiblePropsByMetal()
    {
        Dictionary<string, JObject> combustiblePropsByMetal = new(
            StringComparer.OrdinalIgnoreCase
        );
        List<IAsset> assets = _api.Assets.GetManyInCategory(
            "itemtypes",
            "resource/ingot",
            null,
            true
        );

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            if (root is not JObject ingotType
                || NailAssetJson.TryGetObject(
                    ingotType,
                    "combustiblePropsByType",
                    out JObject combustiblePropsByType
                ) == false)
            {
                continue;
            }

            List<string> excludedTypePatterns = NailAssetJson.LoadExcludedTypePatterns(ingotType);

            foreach (JProperty combustibleEntry in combustiblePropsByType.Properties())
            {
                string metal = NailCode.NormalizeMetal(combustibleEntry.Name);

                if (metal.Length == 0
                    || NailAssetJson.IsExcludedByType(
                        combustibleEntry.Name,
                        excludedTypePatterns
                    )
                    || combustibleEntry.Value is not JObject combustibleProps)
                {
                    continue;
                }

                string smeltedCode = GetStackCode(combustibleProps, "smeltedStack");

                if (NailCode.IsIngotForMetal(smeltedCode, metal))
                {
                    combustiblePropsByMetal[metal] = (JObject)combustibleProps.DeepClone();
                }
            }
        }

        return combustiblePropsByMetal;
    }

    private static bool IsQualifiedIngotMetal(
        string metal,
        Dictionary<string, JObject> ingotCombustiblePropsByMetal,
        HashSet<string> producibleIngotMetals
    )
    {
        string normalizedMetal = NailCode.NormalizeMetal(metal);

        return normalizedMetal.Length > 0
            && ingotCombustiblePropsByMetal.ContainsKey(normalizedMetal)
            && producibleIngotMetals.Contains(normalizedMetal);
    }

    private static string GetStackCode(JObject properties, string stackPropertyName)
    {
        JToken stack = properties[stackPropertyName];

        if (stack == null) return string.Empty;

        return NailAssetJson.GetStringValue(stack["code"]);
    }
}
