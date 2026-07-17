using System;
using BetterNails.src;
using BetterNails.src.patching.rules;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace BetterNails.src.patching.molds;

[HarmonyPatch(
    typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
    nameof(CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo),
    [
        typeof(ItemSlot),
        typeof(ICoreClientAPI),
        typeof(ItemStack[]),
        typeof(ActionConsumable<string>)
    ]
)]
public static class ToolMoldHandbookOutputPatch
{
    private static BetterNailsSystem.ModConfig _config;

    public static void Configure(BetterNailsSystem.ModConfig config)
    {
        _config = config;
    }

    [HarmonyPostfix]
    private static void Postfix(ItemSlot inSlot, RichTextComponentBase[] __result)
    {
        if (_config == null
            || inSlot == null
            || inSlot.Itemstack == null
            || inSlot.Itemstack.Collectible == null
            || inSlot.Itemstack.Collectible.Code == null
            || __result == null) return;

        bool pageMetalFound = NailCode.TryGetNailMetal(
            inSlot.Itemstack.Collectible.Code.Path,
            out string pageMetal);
        if (pageMetalFound == false) return;

        for (int moldIndex = 2; moldIndex < __result.Length - 2; moldIndex++)
        {
            // skip sequences that are not a sized input + tool mold = sized nail output row for this metal
            if (__result[moldIndex - 2]
                    is not SlideshowItemstackTextComponent inputComponent
                || inputComponent.ShowStackSize == false
                || __result[moldIndex - 1] is not RichTextComponent plusComponent
                || HasOperator(plusComponent, "+") == false
                || __result[moldIndex] is not SlideshowItemstackTextComponent moldComponent
                || IsToolMoldComponent(moldComponent) == false
                || __result[moldIndex + 1] is not RichTextComponent equalsComponent
                || HasOperator(equalsComponent, "=") == false
                || __result[moldIndex + 2]
                    is not SlideshowItemstackTextComponent outputComponent
                || outputComponent.ShowStackSize == false
                || HasNailOutputForMetal(outputComponent, pageMetal) == false)
            {
                continue;
            }

            // change only the rendered output; leave the handbook input untouched
            ReplaceNailDisplayStacks(outputComponent, pageMetal);
        }
    }

    private static bool HasOperator(RichTextComponent component, string value)
    {
        if (component.DisplayText == null) return false;

        return string.Equals(component.DisplayText.Trim(), value, StringComparison.Ordinal);
    }

    private static bool IsToolMoldComponent(
        SlideshowItemstackTextComponent component
    )
    {
        if (component.Itemstacks == null || component.Itemstacks.Length == 0) return false;

        foreach (ItemStack stack in component.Itemstacks)
        {
            if (stack == null
                || stack.Collectible == null
                || stack.Collectible is not BlockToolMold)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasNailOutputForMetal(
        SlideshowItemstackTextComponent component,
        string metal
    )
    {
        if (component.Itemstacks == null || component.Itemstacks.Length == 0) return false;

        foreach (ItemStack stack in component.Itemstacks)
        {
            if (stack == null
                || stack.Collectible == null
                || stack.Collectible.Code == null)
            {
                continue;
            }

            bool outputMetalFound = NailCode.TryGetNailMetal(
                stack.Collectible.Code.Path,
                out string outputMetal);
            if (outputMetalFound == false) continue;

            bool metalMatches = string.Equals(
                outputMetal,
                metal,
                StringComparison.OrdinalIgnoreCase);
            if (metalMatches) return true;
        }

        return false;
    }

    private static void ReplaceNailDisplayStacks(
        SlideshowItemstackTextComponent component,
        string metal
    )
    {
        ItemStack[] sourceStacks = component.Itemstacks;

        if (sourceStacks == null || sourceStacks.Length == 0) return;

        ItemStack[] displayStacks = new ItemStack[sourceStacks.Length];
        bool changed = false;

        // clone matching outputs so the handbook's source stacks remain unchanged
        for (int stackIndex = 0; stackIndex < sourceStacks.Length; stackIndex++)
        {
            ItemStack sourceStack = sourceStacks[stackIndex];
            displayStacks[stackIndex] = sourceStack;

            if (sourceStack == null
                || sourceStack.Collectible == null
                || sourceStack.Collectible.Code == null)
            {
                continue;
            }

            bool outputMetalFound = NailCode.TryGetNailMetal(
                sourceStack.Collectible.Code.Path,
                out string outputMetal);
            if (outputMetalFound == false) continue;

            bool metalMatchesPage = string.Equals(
                outputMetal,
                metal,
                StringComparison.OrdinalIgnoreCase);
            if (metalMatchesPage == false) continue;

            ItemStack displayStack = sourceStack.Clone();
            displayStack.StackSize = NailYield.CalculateAmount(
                _config,
                outputMetal,
                sourceStack.StackSize,
                NailYield.VanillaSetsPerIngot
            );
            displayStacks[stackIndex] = displayStack;
            changed = true;
        }

        if (changed)
        {
            component.Itemstacks = displayStacks;
        }
    }
}
