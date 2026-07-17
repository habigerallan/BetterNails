using BetterNails.src;
using BetterNails.src.patching.rules;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace BetterNails.src.patching.molds;

[HarmonyPatch(
    typeof(BlockEntityToolMold),
    nameof(BlockEntityToolMold.GetMoldedStacks),
    [typeof(ItemStack)]
)]
public static class ToolMoldOutputPatch
{
    private static BetterNailsSystem.ModConfig _config;

    public static void Configure(BetterNailsSystem.ModConfig config)
    {
        _config = config;
    }

    [HarmonyPostfix]
    private static void Postfix(ItemStack[] __result)
    {
        AdjustResultStacks(__result, _config);
    }

    public static void AdjustResultStacks(
        ItemStack[] stacks,
        BetterNailsSystem.ModConfig config
    )
    {
        if (stacks == null || config == null) return;

        // adjust resolved drops before take and break paths receive them
        foreach (ItemStack stack in stacks)
        {
            if (stack == null
                || stack.Collectible == null
                || stack.Collectible.Code == null)
            {
                continue;
            }

            bool nailMetalFound = NailCode.TryGetNailMetal(
                stack.Collectible.Code.Path,
                out string metal);
            if (nailMetalFound == false) continue;

            stack.StackSize = NailYield.CalculateAmount(
                config,
                metal,
                stack.StackSize,
                NailYield.VanillaSetsPerIngot
            );
        }
    }
}
