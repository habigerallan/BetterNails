using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterNails.src.patching.models;

public sealed class NailMetalCatalog(
    List<string> metals,
    Dictionary<string, JObject> ingotCombustiblePropsByMetal
    )
{
    public List<string> Metals { get; } = metals;

    public Dictionary<string, JObject> IngotCombustiblePropsByMetal { get; } = ingotCombustiblePropsByMetal;
}
