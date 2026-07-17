using System;
using System.Collections.Generic;
using BetterNails.src.patching.assets;
using BetterNails.src.patching.rules;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.smithing;

public sealed class NailItemPatcher(ICoreAPI api)
{
    private readonly ICoreAPI _api = api;

    public void Patch(
        Dictionary<string, int> amountsByMetal,
        Dictionary<string, JObject> ingotCombustiblePropsByMetal
    )
    {
        List<IAsset> assets = _api.Assets.GetManyInCategory(
            "itemtypes",
            "resource/metalnailsandstrips",
            null,
            true
        );

        foreach (IAsset asset in assets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;
            if (root is not JObject itemType) continue;

            bool assetChanged = PatchNailAllowedVariants(itemType, amountsByMetal);

            if (NailAssetJson.TryGetObject(
                    itemType,
                    "combustiblePropsByType",
                    out JObject combustiblePropsByType
                ) == false)
            {
                combustiblePropsByType = [];
                itemType["combustiblePropsByType"] = combustiblePropsByType;
                assetChanged = true;
            }

            foreach (KeyValuePair<string, int> amountByMetal in amountsByMetal)
            {
                bool combustiblePropsChanged = PatchNailCombustibleProps(
                    combustiblePropsByType,
                    amountByMetal.Key,
                    amountByMetal.Value,
                    ingotCombustiblePropsByMetal
                );

                assetChanged = assetChanged || combustiblePropsChanged;
            }

            if (assetChanged)
            {
                NailAssetJson.Write(asset, root);
            }
        }
    }

    // keep remelting at one ingot per configured nail output
    private static bool PatchNailCombustibleProps(
        JObject combustiblePropsByType,
        string metal,
        int amount,
        Dictionary<string, JObject> ingotCombustiblePropsByMetal
    )
    {
        string itemCode = NailCode.NailOutputPrefix + metal;

        if (NailAssetJson.TryGetObject(
                combustiblePropsByType,
                itemCode,
                out JObject combustibleProps
            ) == false)
        {
            if (ingotCombustiblePropsByMetal.TryGetValue(
                    metal,
                    out JObject ingotCombustibleProps
                ) == false)
            {
                return false;
            }

            combustiblePropsByType[itemCode] = CreateNailCombustibleProps(
                metal,
                amount,
                ingotCombustibleProps
            );
            return true;
        }

        string smeltedCode = GetSmeltedStackCode(combustibleProps);

        if (NailCode.IsIngotForMetal(smeltedCode, metal) == false)
        {
            if (ingotCombustiblePropsByMetal.TryGetValue(
                    metal,
                    out JObject ingotCombustibleProps
                ) == false)
            {
                return false;
            }

            combustiblePropsByType[itemCode] = CreateNailCombustibleProps(
                metal,
                amount,
                ingotCombustibleProps
            );
            return true;
        }

        combustibleProps["smeltedRatio"] = amount;
        return true;
    }

    private static bool PatchNailAllowedVariants(
        JObject itemType,
        Dictionary<string, int> amountsByMetal
    )
    {
        if (itemType["allowedVariants"] is not JArray allowedVariants)
        {
            allowedVariants = [];
            itemType["allowedVariants"] = allowedVariants;
        }

        HashSet<string> existingMetals = [];

        foreach (JToken allowedVariant in allowedVariants)
        {
            string existingMetal = NailCode.NormalizeMetal(
                NailAssetJson.GetStringValue(allowedVariant)
            );

            if (existingMetal.Length > 0)
            {
                existingMetals.Add(existingMetal);
            }
        }

        List<string> sortedMetals = [];

        foreach (string configuredMetal in amountsByMetal.Keys)
        {
            string metal = NailCode.NormalizeMetal(configuredMetal);

            if (metal.Length > 0)
            {
                sortedMetals.Add(metal);
            }
        }

        sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (string metal in sortedMetals)
        {
            if (existingMetals.Add(metal) == false) continue;

            allowedVariants.Add("*-" + metal);
            changed = true;
        }

        return changed;
    }

    private static JObject CreateNailCombustibleProps(
        string metal,
        int amount,
        JObject ingotCombustibleProps
    )
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

    private static string GetSmeltedStackCode(JObject combustibleProps)
    {
        JToken smeltedStack = combustibleProps["smeltedStack"];

        if (smeltedStack == null) return string.Empty;

        return NailAssetJson.GetStringValue(smeltedStack["code"]);
    }
}
