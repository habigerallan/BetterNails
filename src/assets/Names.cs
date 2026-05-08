using System;

namespace BetterNails.src.assets;

internal static class Names
{
    public const string NailOutputCode = "metalnailsandstrips-{metal}";
    public const string NailOutputPrefix = "metalnailsandstrips-";
    public const string MetalPlaceholder = "{metal}";

    public static string NormalizeMetal(string metal)
    {
        string normalizedMetal = metal.Trim().ToLowerInvariant();

        int domainSeparatorIndex = normalizedMetal.LastIndexOf(':');
        if (domainSeparatorIndex >= 0)
        {
            normalizedMetal = normalizedMetal[(domainSeparatorIndex + 1)..];
        }

        normalizedMetal = TrimVariantWildcard(normalizedMetal);

        if (normalizedMetal.StartsWith(NailOutputPrefix, StringComparison.Ordinal))
        {
            normalizedMetal = normalizedMetal[NailOutputPrefix.Length..];
            normalizedMetal = TrimVariantWildcard(normalizedMetal);
        }

        if (normalizedMetal.StartsWith("ingot-", StringComparison.Ordinal))
        {
            normalizedMetal = normalizedMetal["ingot-".Length..];
            normalizedMetal = TrimVariantWildcard(normalizedMetal);
        }

        return normalizedMetal == MetalPlaceholder ? string.Empty : normalizedMetal;
    }

    private static string TrimVariantWildcard(string value)
    {
        if (value.StartsWith("*-", StringComparison.Ordinal))
        {
            return value[2..];
        }

        return value == "*" ? string.Empty : value;
    }
}
