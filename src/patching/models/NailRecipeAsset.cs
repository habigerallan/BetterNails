using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace BetterNails.src.patching.models;

public sealed class NailRecipeAsset(IAsset asset, JToken root, List<string> metals)
{
    public IAsset Asset { get; } = asset;

    public JToken Root { get; } = root;

    public List<string> Metals { get; } = metals;
}
