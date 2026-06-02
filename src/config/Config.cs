using BetterNails.src.assets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterNails.src.config;

public sealed class Config
{
    public const string FileName = "BetterNails.json";
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

    public int DefaultAmount { get; set; } = VanillaSetsPerIngot;

    public Dictionary<string, int> Metals { get; set; } = CreateDefaultMetalAmounts();

    public void EnsureDefaults()
    {
        if (DefaultAmount < 1)
        {
            DefaultAmount = VanillaSetsPerIngot;
        }

        Metals ??= [];

        foreach ((string metal, int amount) in DefaultMetalAmounts)
        {
            if (ContainsMetal(metal))
            {
                continue;
            }

            Metals[metal] = amount;
        }
    }

    public bool AddDiscoveredMetals(IEnumerable<string> metals)
    {
        bool changed = false;

        foreach (string metal in metals.Select(Names.NormalizeMetal).Where(metal => metal.Length > 0))
        {
            if (ContainsMetal(metal))
            {
                continue;
            }

            Metals[metal] = GetDefaultAmountForMetal(metal);
            changed = true;
        }

        return changed;
    }

    public int GetAmountForMetal(string metal)
    {
        string normalizedMetal = Names.NormalizeMetal(metal);

        foreach ((string configuredMetal, int amount) in Metals)
        {
            if (string.Equals(Names.NormalizeMetal(configuredMetal), normalizedMetal, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeAmount(amount);
            }
        }

        return GetDefaultAmountForMetal(normalizedMetal);
    }

    private bool ContainsMetal(string metal)
    {
        string normalizedMetal = Names.NormalizeMetal(metal);
        return Metals.Keys.Any(configuredMetal =>
            string.Equals(Names.NormalizeMetal(configuredMetal), normalizedMetal, StringComparison.OrdinalIgnoreCase)
        );
    }

    private int GetDefaultAmountForMetal(string metal)
    {
        string normalizedMetal = Names.NormalizeMetal(metal);

        if (DefaultMetalAmounts.TryGetValue(normalizedMetal, out int amount))
        {
            return amount;
        }

        return NormalizeAmount(DefaultAmount);
    }

    private int NormalizeAmount(int amount)
    {
        if (amount >= 1)
        {
            return amount;
        }

        return DefaultAmount >= 1 ? DefaultAmount : VanillaSetsPerIngot;
    }

    private static Dictionary<string, int> CreateDefaultMetalAmounts()
    {
        return new Dictionary<string, int>(DefaultMetalAmounts, StringComparer.OrdinalIgnoreCase);
    }
}
