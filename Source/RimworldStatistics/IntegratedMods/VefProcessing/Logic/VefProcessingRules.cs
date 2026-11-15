namespace RimworldStatistics.VefProcessing;

internal static class VefProcessingRules
{
    internal const string CookingUnit = "mod.vanillaexpanded.vcooke.cookingprocess";
    internal const string BrewingUnit = "mod.vanillaexpanded.vbrewe.brewingprocess";
    internal const string StewsUnit = "mod.vanillaexpanded.vcookestews.stewprocess";
    internal static bool CompleteBatch(int before, int after, bool ownerMatches, bool player)
        => ownerMatches && player && after == before + 1;
}
