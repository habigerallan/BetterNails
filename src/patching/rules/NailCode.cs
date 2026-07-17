using System;

namespace BetterNails.src.patching.rules;

public static class NailCode
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

        if (normalizedMetal == MetalPlaceholder)
        {
            normalizedMetal = string.Empty;
        }

        return normalizedMetal;
    }

    public static bool IsNailOutputCode(string outputCode)
    {
        if (string.IsNullOrWhiteSpace(outputCode)) return false;

        string normalizedCode = outputCode.ToLowerInvariant();
        return normalizedCode == NailOutputCode
            || normalizedCode.StartsWith(NailOutputPrefix, StringComparison.Ordinal);
    }

    public static bool UsesMetalPlaceholder(string outputCode)
    {
        if (outputCode == null) return false;

        return outputCode.Contains(MetalPlaceholder, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetNailMetal(string itemPath, out string metal)
    {
        metal = string.Empty;

        if (string.IsNullOrWhiteSpace(itemPath)) return false;

        string normalizedPath = itemPath.ToLowerInvariant();

        if (normalizedPath.StartsWith(NailOutputPrefix, StringComparison.Ordinal) == false) return false;

        metal = NormalizeMetal(normalizedPath[NailOutputPrefix.Length..]);
        return metal.Length > 0;
    }

    public static bool TryGetIngotMetal(string itemCode, out string metal)
    {
        metal = string.Empty;

        if (string.IsNullOrWhiteSpace(itemCode)) return false;

        string normalizedCode = itemCode.Trim().ToLowerInvariant();
        int domainSeparatorIndex = normalizedCode.LastIndexOf(':');

        if (domainSeparatorIndex >= 0)
        {
            normalizedCode = normalizedCode[(domainSeparatorIndex + 1)..];
        }

        if (normalizedCode.StartsWith("ingot-", StringComparison.Ordinal) == false
            || normalizedCode.Contains('{', StringComparison.Ordinal)
            || normalizedCode.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        metal = NormalizeMetal(normalizedCode);
        return metal.Length > 0;
    }

    public static bool IsIngotForMetal(string itemCode, string metal)
    {
        if (string.IsNullOrWhiteSpace(itemCode)) return false;

        string expectedCode = "ingot-" + metal;
        string normalizedCode = itemCode.Trim().ToLowerInvariant();

        return normalizedCode == expectedCode
            || normalizedCode.EndsWith(":" + expectedCode, StringComparison.Ordinal);
    }

    private static string TrimVariantWildcard(string value)
    {
        if (value.StartsWith("*-", StringComparison.Ordinal)) return value[2..];

        if (value == "*")
        {
            value = string.Empty;
        }

        return value;
    }
}
