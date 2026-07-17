using System.Collections.Generic;
using BetterNails.src;
using BetterNails.src.patching.models;
using BetterNails.src.patching.rules;
using BetterNails.src.patching.smithing;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.assets;

public sealed class NailYieldsPatcher
{
    private readonly ICoreAPI _api;
    private readonly BetterNailsSystem.ModConfig _config;

    public NailYieldsPatcher(ICoreAPI api, BetterNailsSystem.ModConfig config)
    {
        _api = api;
        _config = config;
    }

    public bool Patch()
    {
        NailSmithingPatcher smithingPatcher = new(_api);
        List<NailRecipeAsset> nailRecipeAssets = smithingPatcher.LoadRecipeAssets();

        if (nailRecipeAssets.Count == 0) return false;

        NailMetalCatalog metalCatalog = new NailMetalDiscovery(_api).Discover(nailRecipeAssets);
        bool addedDiscoveredMetals = NailYield.AddDiscoveredMetals(_config, metalCatalog.Metals);
        Dictionary<string, int> amountsByMetal = NailYield.BuildAmountMap(
            _config,
            metalCatalog.Metals
        );

        smithingPatcher.Patch(nailRecipeAssets, amountsByMetal, metalCatalog.Metals);
        new NailItemPatcher(_api).Patch(
            amountsByMetal,
            metalCatalog.IngotCombustiblePropsByMetal
        );

        return addedDiscoveredMetals;
    }
}
