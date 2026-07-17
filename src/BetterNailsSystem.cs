using System.Collections.Generic;
using BetterNails.src.patching.assets;
using BetterNails.src.patching.molds;
using BetterNails.src.patching.rules;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace BetterNails.src;

public sealed class BetterNailsSystem : ModSystem
{
    public sealed class ModConfig
    {
        public int DefaultAmount = NailYield.VanillaSetsPerIngot;
        public Dictionary<string, int> Metals = NailYield.CreateDefaultMetalAmounts();

        public static ModConfig Load(ICoreAPI api, string fileName)
        {
            ModConfig config = api.LoadModConfig<ModConfig>(fileName) ?? new ModConfig();
            config.EnsureDefaults();
            api.StoreModConfig(config, fileName);

            return config;
        }

        private void EnsureDefaults()
        {
            if (DefaultAmount < 1) DefaultAmount = NailYield.VanillaSetsPerIngot;

            Metals ??= [];
            Dictionary<string, int> defaultMetalAmounts = NailYield.CreateDefaultMetalAmounts();

            foreach (KeyValuePair<string, int> defaultMetalAmount in defaultMetalAmounts)
            {
                if (NailYield.ContainsMetal(Metals, defaultMetalAmount.Key)) continue;

                Metals[defaultMetalAmount.Key] = defaultMetalAmount.Value;
            }
        }
    }

    private const string ConfigFileName = "BetterNails.json";
    private const string HarmonyId = "betternails-nailyields";

    private ModConfig _config = new();
    private Harmony _harmony;

    public override double ExecuteOrder()
    {
        // after the vanilla json patch loader, before block/item/recipe loading
        return 0.11;
    }

    public override void Start(ICoreAPI api)
    {
        _config = ModConfig.Load(api, ConfigFileName);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        bool addedDiscoveredMetals = new NailYieldsPatcher(api, _config).Patch();

        if (addedDiscoveredMetals)
        {
            api.StoreModConfig(_config, ConfigFileName);
        }
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        _harmony = new Harmony(HarmonyId);
        ToolMoldHandbookOutputPatch.Configure(_config);
        _harmony.CreateClassProcessor(typeof(ToolMoldHandbookOutputPatch)).Patch();
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        _harmony = new Harmony(HarmonyId);
        ToolMoldOutputPatch.Configure(_config);
        _harmony.CreateClassProcessor(typeof(ToolMoldOutputPatch)).Patch();
    }

    public override void Dispose()
    {
        if (_harmony == null) return;

        _harmony.UnpatchAll(HarmonyId);
        _harmony = null;
    }
}
