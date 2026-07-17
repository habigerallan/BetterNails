using System;
using System.Collections.Generic;
using BetterNails.src.patching.assets;
using BetterNails.src.patching.models;
using BetterNails.src.patching.rules;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.smithing;

public sealed class NailSmithingPatcher(ICoreAPI api)
{
    private readonly ICoreAPI _api = api;

    public List<NailRecipeAsset> LoadRecipeAssets()
    {
        List<NailRecipeAsset> nailRecipeAssets = [];
        List<IAsset> smithingAssets = _api.Assets.GetManyInCategory(
            "recipes",
            "smithing",
            null,
            true
        );

        foreach (IAsset asset in smithingAssets)
        {
            if (NailAssetJson.TryParse(asset, out JToken root) == false) continue;

            List<string> metals = [];
            HashSet<string> existingMetals = new(StringComparer.OrdinalIgnoreCase);
            List<JObject> recipeObjects = NailAssetJson.GetRecipeObjects(root);

            foreach (JObject recipeObject in recipeObjects)
            {
                if (IsNailRecipe(recipeObject) == false) continue;

                List<string> recipeMetals = GetRecipeMetals(recipeObject);

                foreach (string recipeMetal in recipeMetals)
                {
                    string metal = NailCode.NormalizeMetal(recipeMetal);

                    if (metal.Length == 0 || existingMetals.Add(metal) == false) continue;

                    metals.Add(metal);
                }
            }

            if (metals.Count > 0)
            {
                nailRecipeAssets.Add(new NailRecipeAsset(asset, root, metals));
            }
        }

        return nailRecipeAssets;
    }

    public void Patch(
        List<NailRecipeAsset> nailRecipeAssets,
        Dictionary<string, int> amountsByMetal,
        List<string> qualifiedMetals
    )
    {
        int baseSetsPerIngot = FindBaseSetsPerIngot(nailRecipeAssets);

        foreach (NailRecipeAsset nailRecipeAsset in nailRecipeAssets)
        {
            JToken patchedRoot = PatchRecipeRoot(
                nailRecipeAsset.Root,
                amountsByMetal,
                qualifiedMetals,
                baseSetsPerIngot
            );

            NailAssetJson.Write(nailRecipeAsset.Asset, patchedRoot);
        }
    }

    private static JToken PatchRecipeRoot(
        JToken root,
        Dictionary<string, int> amountsByMetal,
        List<string> qualifiedMetals,
        int baseSetsPerIngot
    )
    {
        if (root is JArray recipeArray)
        {
            JArray patchedArray = [];

            foreach (JToken recipeToken in recipeArray)
            {
                List<JToken> patchedRecipes = PatchRecipeToken(
                    recipeToken,
                    amountsByMetal,
                    qualifiedMetals,
                    baseSetsPerIngot
                );

                foreach (JToken patchedRecipe in patchedRecipes)
                {
                    patchedArray.Add(patchedRecipe);
                }
            }

            return patchedArray;
        }

        if (root is JObject recipeObject)
        {
            List<JToken> patchedRecipes = PatchRecipeToken(
                recipeObject,
                amountsByMetal,
                qualifiedMetals,
                baseSetsPerIngot
            );

            JToken patchedRoot = new JArray(patchedRecipes);

            if (patchedRecipes.Count == 1)
            {
                patchedRoot = patchedRecipes[0];
            }

            return patchedRoot;
        }

        return root;
    }

    private static List<JToken> PatchRecipeToken(
        JToken recipeToken,
        Dictionary<string, int> amountsByMetal,
        List<string> qualifiedMetals,
        int baseSetsPerIngot
    )
    {
        if (recipeToken is not JObject recipe || IsNailRecipe(recipe) == false)
        {
            return [recipeToken.DeepClone()];
        }

        string outputCode = GetOutputCode(recipe);
        bool usesMetalPlaceholder = NailCode.UsesMetalPlaceholder(outputCode);
        HashSet<string> qualifiedMetalSet = [];

        foreach (string qualifiedMetal in qualifiedMetals)
        {
            string normalizedMetal = NailCode.NormalizeMetal(qualifiedMetal);

            if (normalizedMetal.Length > 0)
            {
                qualifiedMetalSet.Add(normalizedMetal);
            }
        }

        List<string> originalMetals = [];
        HashSet<string> originalMetalSet = new(StringComparer.OrdinalIgnoreCase);
        List<string> recipeMetals = GetRecipeMetals(recipe);

        foreach (string recipeMetal in recipeMetals)
        {
            string normalizedMetal = NailCode.NormalizeMetal(recipeMetal);

            if (normalizedMetal.Length == 0 || originalMetalSet.Add(normalizedMetal) == false)
            {
                continue;
            }

            originalMetals.Add(normalizedMetal);
        }

        List<string> metals = [];
        HashSet<string> metalSet = new(StringComparer.OrdinalIgnoreCase);

        if (usesMetalPlaceholder)
        {
            foreach (string originalMetal in originalMetals)
            {
                if (qualifiedMetalSet.Contains(originalMetal) == false) continue;
                if (metalSet.Add(originalMetal) == false) continue;

                metals.Add(originalMetal);
            }

            foreach (string qualifiedMetal in qualifiedMetals)
            {
                string normalizedMetal = NailCode.NormalizeMetal(qualifiedMetal);

                if (normalizedMetal.Length == 0) continue;
                if (qualifiedMetalSet.Contains(normalizedMetal) == false) continue;
                if (metalSet.Add(normalizedMetal) == false) continue;

                metals.Add(normalizedMetal);
            }
        }
        else
        {
            foreach (string originalMetal in originalMetals)
            {
                if (qualifiedMetalSet.Contains(originalMetal) == false) continue;

                metals.Add(originalMetal);
            }
        }

        if (metals.Count == 0) return [];

        int originalStackSize = NailAssetJson.GetOutputStackSize(recipe);

        if (metals.Count <= 1)
        {
            JObject patchedRecipe = (JObject)recipe.DeepClone();
            string metal = string.Empty;

            if (metals.Count == 1)
            {
                metal = metals[0];
            }

            int amount = NailYield.CalculateAmount(
                amountsByMetal,
                metal,
                originalStackSize,
                baseSetsPerIngot
            );

            if (usesMetalPlaceholder)
            {
                SetMetalAllowedVariants(patchedRecipe, metals);
            }

            NailAssetJson.SetOutputStackSize(patchedRecipe, amount);
            return [patchedRecipe];
        }

        // split shared recipes when configured metals produce different stack sizes
        SortedDictionary<int, List<string>> amountGroups = [];

        foreach (string metal in metals)
        {
            int amount = NailYield.CalculateAmount(
                amountsByMetal,
                metal,
                originalStackSize,
                baseSetsPerIngot
            );

            if (amountGroups.TryGetValue(amount, out List<string> amountMetals) == false)
            {
                amountMetals = [];
                amountGroups[amount] = amountMetals;
            }

            amountMetals.Add(metal);
        }

        if (amountGroups.Count == 1)
        {
            int amount = 0;
            List<string> amountMetals = [];

            foreach (KeyValuePair<int, List<string>> amountGroup in amountGroups)
            {
                amount = amountGroup.Key;
                amountMetals = amountGroup.Value;
                break;
            }

            JObject patchedRecipe = (JObject)recipe.DeepClone();

            if (usesMetalPlaceholder)
            {
                List<string> sortedMetals = new(amountMetals);
                sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);
                SetMetalAllowedVariants(patchedRecipe, sortedMetals);
            }

            NailAssetJson.SetOutputStackSize(patchedRecipe, amount);
            return [patchedRecipe];
        }

        List<JToken> splitRecipes = [];

        foreach (KeyValuePair<int, List<string>> amountGroup in amountGroups)
        {
            JObject patchedRecipe = (JObject)recipe.DeepClone();
            List<string> sortedMetals = new(amountGroup.Value);
            sortedMetals.Sort(StringComparer.OrdinalIgnoreCase);

            SetMetalAllowedVariants(patchedRecipe, sortedMetals);
            NailAssetJson.SetOutputStackSize(patchedRecipe, amountGroup.Key);
            splitRecipes.Add(patchedRecipe);
        }

        return splitRecipes;
    }

    private static List<string> GetRecipeMetals(JObject recipe)
    {
        List<string> metals = [];
        string outputCode = GetOutputCode(recipe);

        if (string.IsNullOrWhiteSpace(outputCode)) return metals;

        if (NailCode.UsesMetalPlaceholder(outputCode) == false)
        {
            if (NailCode.TryGetNailMetal(outputCode, out string metal))
            {
                metals.Add(metal);
            }

            return metals;
        }

        JObject metalIngredient = FindMetalIngredient(recipe);

        if (metalIngredient == null
            || metalIngredient["allowedVariants"] is not JArray allowedVariants)
        {
            return metals;
        }

        foreach (JToken variant in allowedVariants)
        {
            string metal = NailAssetJson.GetStringValue(variant);

            if (string.IsNullOrWhiteSpace(metal) == false)
            {
                metals.Add(metal);
            }
        }

        return metals;
    }

    private static JObject FindMetalIngredient(JObject recipe)
    {
        if (recipe["ingredient"] is JObject ingredient
            && IsMetalVariantSource(ingredient))
        {
            return ingredient;
        }

        foreach (JToken descendant in recipe.Descendants())
        {
            if (descendant is JObject descendantObject
                && IsMetalVariantSource(descendantObject))
            {
                return descendantObject;
            }
        }

        return null;
    }

    private static bool IsMetalVariantSource(JObject value)
    {
        if (value["allowedVariants"] is not JArray) return false;

        string name = NailAssetJson.GetStringValue(value["name"]);
        return string.Equals(name, "metal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNailRecipe(JObject recipe)
    {
        return NailCode.IsNailOutputCode(GetOutputCode(recipe));
    }

    private static string GetOutputCode(JObject recipe)
    {
        JToken output = recipe["output"];

        if (output == null) return string.Empty;

        return NailAssetJson.GetStringValue(output["code"]);
    }

    private static void SetMetalAllowedVariants(JObject recipe, List<string> metals)
    {
        JObject metalIngredient = FindMetalIngredient(recipe);

        if (metalIngredient != null)
        {
            metalIngredient["allowedVariants"] = new JArray(metals);
        }
    }

    private static int FindBaseSetsPerIngot(List<NailRecipeAsset> nailRecipeAssets)
    {
        int baseSetsPerIngot = 0;

        foreach (NailRecipeAsset nailRecipeAsset in nailRecipeAssets)
        {
            List<JObject> recipeObjects = NailAssetJson.GetRecipeObjects(nailRecipeAsset.Root);

            foreach (JObject recipeObject in recipeObjects)
            {
                if (IsNailRecipe(recipeObject) == false) continue;

                int stackSize = NailAssetJson.GetOutputStackSize(recipeObject);

                if (stackSize <= 0) continue;
                if (baseSetsPerIngot > 0 && stackSize >= baseSetsPerIngot) continue;

                baseSetsPerIngot = stackSize;
            }
        }

        if (baseSetsPerIngot <= 0)
        {
            baseSetsPerIngot = NailYield.VanillaSetsPerIngot;
        }

        return baseSetsPerIngot;
    }
}
