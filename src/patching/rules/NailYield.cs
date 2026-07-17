using System;
using System.Collections.Generic;
using BetterNails.src;

namespace BetterNails.src.patching.rules;

public static class NailYield
{
    public const int VanillaSetsPerIngot = 4;

    private static readonly Dictionary<string, int> DefaultMetalAmounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lead"] = 2,
        ["tin"] = 2,
        ["zinc"] = 2,
        ["bismuth"] = 2,
        ["molybdochalkos"] = 2,
        ["copper"] = 4,
        ["cupronickel"] = 8,
        ["silver"] = 3,
        ["electrum"] = 3,
        ["gold"] = 3,
        ["nickel"] = 4,
        ["bismuthbronze"] = 6,
        ["blackbronze"] = 6,
        ["brass"] = 5,
        ["tinbronze"] = 5,
        ["iron"] = 8,
        ["meteoriciron"] = 10,
        ["steel"] = 12,
        ["meteoricsteel"] = 14
    };

    public static Dictionary<string, int> CreateDefaultMetalAmounts()
    {
        return new Dictionary<string, int>(DefaultMetalAmounts, StringComparer.OrdinalIgnoreCase);
    }

    public static bool ContainsMetal(Dictionary<string, int> amountsByMetal, string metal)
    {
        string normalizedMetal = NailCode.NormalizeMetal(metal);

        foreach (string configuredMetal in amountsByMetal.Keys)
        {
            if (string.Equals(
                    NailCode.NormalizeMetal(configuredMetal),
                    normalizedMetal,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AddDiscoveredMetals(
        BetterNailsSystem.ModConfig config,
        List<string> metals
    )
    {
        bool changed = false;

        foreach (string metal in metals)
        {
            string normalizedMetal = NailCode.NormalizeMetal(metal);

            if (normalizedMetal.Length == 0
                || ContainsMetal(config.Metals, normalizedMetal))
            {
                continue;
            }

            config.Metals[normalizedMetal] = GetDefaultAmountForMetal(config, normalizedMetal);
            changed = true;
        }

        return changed;
    }

    public static Dictionary<string, int> BuildAmountMap(
        BetterNailsSystem.ModConfig config,
        List<string> metals
    )
    {
        Dictionary<string, int> amountsByMetal = new(StringComparer.OrdinalIgnoreCase);

        foreach (string metal in metals)
        {
            string normalizedMetal = NailCode.NormalizeMetal(metal);

            if (normalizedMetal.Length == 0) continue;

            amountsByMetal[normalizedMetal] = GetAmountForMetal(config, normalizedMetal);
        }

        return amountsByMetal;
    }

    public static int GetAmountForMetal(BetterNailsSystem.ModConfig config, string metal)
    {
        string normalizedMetal = NailCode.NormalizeMetal(metal);

        foreach (KeyValuePair<string, int> configuredAmount in config.Metals)
        {
            if (string.Equals(
                    NailCode.NormalizeMetal(configuredAmount.Key),
                    normalizedMetal,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return NormalizeAmount(config, configuredAmount.Value);
            }
        }

        return GetDefaultAmountForMetal(config, normalizedMetal);
    }

    public static int CalculateAmount(
        Dictionary<string, int> amountsByMetal,
        string metal,
        int originalStackSize,
        int baseSetsPerIngot
    )
    {
        int configuredAmount = VanillaSetsPerIngot;

        if (amountsByMetal.TryGetValue(metal, out int amount))
        {
            configuredAmount = amount;
        }

        return ScaleAmount(configuredAmount, originalStackSize, baseSetsPerIngot);
    }

    public static int CalculateAmount(
        BetterNailsSystem.ModConfig config,
        string metal,
        int originalStackSize,
        int baseSetsPerIngot
    )
    {
        int configuredAmount = GetAmountForMetal(config, metal);
        return ScaleAmount(configuredAmount, originalStackSize, baseSetsPerIngot);
    }

    private static int GetDefaultAmountForMetal(
        BetterNailsSystem.ModConfig config,
        string metal
    )
    {
        string normalizedMetal = NailCode.NormalizeMetal(metal);

        if (DefaultMetalAmounts.TryGetValue(normalizedMetal, out int amount)) return amount;

        return NormalizeAmount(config, config.DefaultAmount);
    }

    private static int NormalizeAmount(BetterNailsSystem.ModConfig config, int amount)
    {
        if (amount >= 1) return amount;

        int normalizedAmount = VanillaSetsPerIngot;

        if (config.DefaultAmount >= 1)
        {
            normalizedAmount = config.DefaultAmount;
        }

        return normalizedAmount;
    }

    private static int ScaleAmount(int configuredAmount, int originalStackSize, int baseSetsPerIngot)
    {
        // preserve the source recipe or mold multiplier relative to its per-ingot baseline
        decimal ingotMultiplier = 1m;

        if (baseSetsPerIngot > 0)
        {
            ingotMultiplier = originalStackSize / (decimal)baseSetsPerIngot;
        }

        return Math.Max(
            1,
            (int)Math.Round(configuredAmount * ingotMultiplier, MidpointRounding.AwayFromZero)
        );
    }
}
