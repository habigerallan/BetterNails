using BetterNails.src.config;
using Vintagestory.API.Common;

namespace BetterNails.src;

public sealed class BetterNailsSystem : ModSystem
{
    private Config config = new();

    public override double ExecuteOrder()
    {
        // After the vanilla JSON patch loader, before block/item/recipe loading.
        return 0.11;
    }

    public override void Start(ICoreAPI api)
    {
        config = api.LoadModConfig<Config>(Config.FileName) ?? new Config();
        config.EnsureDefaults();

        api.StoreModConfig(config, Config.FileName);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        new assets.Patcher(api, config).Patch();
    }
}
