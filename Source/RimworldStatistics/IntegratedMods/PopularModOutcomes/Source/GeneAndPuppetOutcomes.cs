using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.PopularModOutcomes;

internal static class GeneAndPuppetOutcomes
{
    private const string Stinger = "mod.sarg.alphagenes.stinger_implants";
    private const string Puppet = "mod.vanillaexpanded.vpe.puppeteer.puppets_created";
    private const string Transfer = "mod.vanillaexpanded.vpe.puppeteer.minds_transferred";
    private const string PuppeteerPackage = "VanillaExpanded.VPE.Puppeteer";
    private const string PsycastsPackage = "VanillaExpanded.VPsycastsE";
    private const string PuppeteerAssembly = "VPEPuppeteer";
    private static Type puppetHediff = null!;
    private static Type puppeteerHediff = null!;
    private static FieldInfo master = null!;

    internal static ModMetricAdapterRegistration GeneImplants() =>
        OutcomeMetric.Count("sarg.alphagenes", "stinger_implants", "RWS_StingerImplants", "RWS_StingerImplantsDesc",
            MetricCategory.Biotech, InstallStinger, dlc: AcquisitionUnits.Biotech);

    internal static ModMetricAdapterRegistration Puppets() =>
        OutcomeMetric.Count(PuppeteerPackage, "puppets_created", "RWS_PuppetsCreated", "RWS_PuppetsCreatedDesc",
            MetricCategory.Royalty, InstallPuppets, dlc: AcquisitionUnits.Royalty, dependencies: [PsycastsPackage]);

    internal static ModMetricAdapterRegistration MindTransfers() =>
        OutcomeMetric.Count(PuppeteerPackage, "minds_transferred", "RWS_PuppetTransfers", "RWS_PuppetTransfersDesc",
            MetricCategory.Royalty, InstallTransfers, dlc: AcquisitionUnits.Royalty, dependencies: [PsycastsPackage]);

    private static void InstallPuppets(Harmony harmony)
    {
        var ability = OutcomeMetric.Bind("VPEPuppeteer.Ability_Puppet", PuppeteerAssembly);
        puppetHediff = OutcomeMetric.Bind("VPEPuppeteer.Hediff_Puppet", PuppeteerAssembly);
        if (AccessTools.Field(ability, "pawn")?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("Puppeteer caster field changed");
        master = AccessTools.Field(puppetHediff, "master") is { } field && field.FieldType == typeof(Pawn)
            ? field : throw new InvalidOperationException("Puppeteer master field changed");
        var cast = OutcomeMetric.Method(ability, "Cast", typeof(void), typeof(GlobalTargetInfo[]));
        OutcomeMetric.Patch(harmony, cast, typeof(GeneAndPuppetOutcomes), nameof(BeforePuppet), nameof(AfterPuppet));
    }

    private static Hediff? ExactHediff(Pawn pawn, Type type)
        => pawn.health?.hediffSet?.hediffs.FirstOrDefault(hediff => hediff?.GetType() == type);

    private static void BeforePuppet(Pawn? ___pawn, GlobalTargetInfo[]? __0, out Pawn? __state)
    {
        __state = null;
        if (OutcomeMetric.Observing(Puppet) && GameFacts.IsPlayer(___pawn) && __0 is { Length: > 0 } &&
            __0[0].Thing is Pawn { Dead: false } target && ExactHediff(target, puppetHediff) is null)
            __state = target;
    }

    private static void AfterPuppet(Pawn? ___pawn, Pawn? __state, bool __runOriginal)
    {
        if (!__runOriginal || __state is not { Dead: false } target || !GameFacts.IsPlayer(target) ||
            !OutcomeMetric.Observing(Puppet))
            return;
        try
        {
            if (ExactHediff(target, puppetHediff) is { } created && ReferenceEquals(master.GetValue(created), ___pawn))
                OutcomeMetric.Record(Puppet);
        }
        catch (Exception error) { LiveMetrics.Fail(Puppet, error); }
    }

    private static void InstallTransfers(Harmony harmony)
    {
        var jump = OutcomeMetric.Bind("VPEPuppeteer.MindJump", PuppeteerAssembly);
        puppeteerHediff = OutcomeMetric.Bind("VPEPuppeteer.Hediff_Puppeteer", PuppeteerAssembly);
        var transfer = OutcomeMetric.Method(jump, "TransferMind", typeof(void), typeof(Pawn), typeof(Pawn));
        OutcomeMetric.Patch(harmony, transfer, typeof(GeneAndPuppetOutcomes), nameof(BeforeTransfer), nameof(AfterTransfer));
    }

    private static void BeforeTransfer(Pawn? __0, Pawn? __1, out Hediff? __state)
    {
        __state = null;
        if (__0 is { Dead: false } && __1 is not null && !ReferenceEquals(__0, __1) &&
            GameFacts.IsPlayer(__1) && OutcomeMetric.Observing(Transfer))
            __state = ExactHediff(__1, puppeteerHediff);
    }

    private static void AfterTransfer(Pawn __0, Pawn __1, Hediff? __state, bool __runOriginal)
    {
        if (__runOriginal && __state is not null && !__0.Dead && GameFacts.IsPlayer(__0) &&
            OutcomeMetric.Observing(Transfer) &&
            ReferenceEquals(ExactHediff(__0, puppeteerHediff), __state) &&
            !ReferenceEquals(ExactHediff(__1, puppeteerHediff), __state))
            OutcomeMetric.Record(Transfer);
    }

    private static void InstallStinger(Harmony harmony)
    {
        var effect = OutcomeMetric.Bind("AlphaGenes.CompAbilityEffect_Stinger", "AlphaGenes");
        var implant = OutcomeMetric.Method(effect, "ReimplantXenogerm", typeof(void), typeof(Pawn), typeof(Pawn));
        OutcomeMetric.Patch(harmony, implant, typeof(GeneAndPuppetOutcomes), nameof(BeforeImplant), nameof(AfterImplant));
    }

    private sealed record GeneState(XenotypeDef? Xenotype, string? Name, HashSet<GeneDef> Endogenes, HashSet<GeneDef> Xenogenes);

    private static void BeforeImplant(Pawn? __0, Pawn? __1, out GeneState? __state)
    {
        __state = null;
        if (!GameFacts.IsPlayer(__0) || __1 is not { Dead: false, genes: { } target } ||
            !OutcomeMetric.Observing(Stinger))
            return;
        __state = new GeneState(target.Xenotype, target.xenotypeName, target.Endogenes.Select(gene => gene.def).ToHashSet(),
            target.Xenogenes.Select(gene => gene.def).ToHashSet());
    }

    private static void AfterImplant(Pawn __1, GeneState? __state, bool __runOriginal)
    {
        if (__runOriginal && __state is not null && __1 is { Dead: false, genes: { } actual } &&
            OutcomeMetric.Observing(Stinger) &&
            (__state.Xenotype != actual.Xenotype || !string.Equals(__state.Name, actual.xenotypeName, StringComparison.Ordinal) ||
             !__state.Endogenes.SetEquals(actual.Endogenes.Select(gene => gene.def)) ||
             !__state.Xenogenes.SetEquals(actual.Xenogenes.Select(gene => gene.def))))
            OutcomeMetric.Record(Stinger);
    }
}
