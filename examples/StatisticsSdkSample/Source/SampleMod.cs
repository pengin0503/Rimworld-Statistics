using Verse;

namespace StatisticsSdkSample;

/// <summary>Independent RimWorld MOD. The Statistics host remains an optional load-after dependency.</summary>
public sealed class SampleMod : Mod
{
    public SampleMod(ModContentPack content) : base(content)
    {
        SampleSdkClient.TryAttach();
    }
}
