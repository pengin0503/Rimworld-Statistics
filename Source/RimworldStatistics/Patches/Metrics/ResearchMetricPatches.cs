using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Phase C · research (evidence #research). The outer progress calls record the project's state before progress changes;
/// FinishProject decides "not finished → finished" from that state (or its own entry state for a direct completion or a
/// prerequisite). Progress is read from the manager's own dictionaries, without GetProgress's side effect of adding entries.
/// </summary>
internal static class ResearchProbe
{
    public const string Unit = ResearchFamily.Name;
    public static readonly ResearchHooks Hooks = new();

    public sealed class Scope
    {
        public required ResearchHooks.Frame Frame { get; init; }
        public required ResearchProjectDef Def { get; init; }
    }

    public static double Progress(Dictionary<ResearchProjectDef, float> progress, Dictionary<ResearchProjectDef, float> knowledge, ResearchProjectDef p)
    {
        // As ResearchManager.GetProgress (1.7.4985): base-cost progress, else Anomaly knowledge, else 0.
        if (p.baseCost > 0f)
            return progress != null && progress.TryGetValue(p, out var v) ? v : 0;
        if (ModsConfig.AnomalyActive && p.knowledgeCost > 0f)
            return knowledge != null && knowledge.TryGetValue(p, out var k) ? k : 0;
        return 0;
    }

    public static bool Finished(Dictionary<ResearchProjectDef, float> progress, Dictionary<ResearchProjectDef, float> knowledge, ResearchProjectDef p)
        => Progress(progress, knowledge, p) >= p.Cost;   // ResearchProjectDef.IsFinished

    public static Scope? Begin(ResearchProjectDef? def, Dictionary<ResearchProjectDef, float> progress, Dictionary<ResearchProjectDef, float> knowledge)
    {
        if (def is null || LiveMetrics.Family(Unit) is null)
            return null;
        try
        {
            return new Scope { Def = def, Frame = Hooks.OuterBegin(def.defName, Finished(progress, knowledge, def), Progress(progress, knowledge, def)) };
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
            return null;
        }
    }

    public static void End(Scope? scope, Dictionary<ResearchProjectDef, float> progress, Dictionary<ResearchProjectDef, float> knowledge)
    {
        if (scope is null)
            return;
        try
        {
            Hooks.OuterEnd(LiveMetrics.Family(Unit), scope.Frame, Progress(progress, knowledge, scope.Def), Find.TickManager.TicksGame);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}

[MetricAdapter(ResearchFamily.Name)]
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
public static class ResearchPerformedMetricPatch
{
    public static void Prefix(ResearchProjectDef ___currentProj, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge, out object? __state)
        => __state = ResearchProbe.Begin(___currentProj, ___progress, ___anomalyKnowledge);

    public static Exception? Finalizer(Exception? __exception, object? __state, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge)
    {
        ResearchProbe.End(__state as ResearchProbe.Scope, ___progress, ___anomalyKnowledge);
        return __exception;
    }
}

[MetricAdapter(ResearchFamily.Name)]
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.AddProgress))]
public static class ResearchAddProgressMetricPatch
{
    public static void Prefix(ResearchProjectDef proj, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge, out object? __state)
        => __state = ResearchProbe.Begin(proj, ___progress, ___anomalyKnowledge);

    public static Exception? Finalizer(Exception? __exception, object? __state, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge)
    {
        ResearchProbe.End(__state as ResearchProbe.Scope, ___progress, ___anomalyKnowledge);
        return __exception;
    }
}

[MetricAdapter(ResearchFamily.Name)]
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.ApplyKnowledge),
    new[] { typeof(ResearchProjectDef), typeof(float), typeof(float) }, new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
public static class ResearchApplyKnowledgeMetricPatch
{
    public static void Prefix(ResearchProjectDef project, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge, out object? __state)
        => __state = ResearchProbe.Begin(project, ___progress, ___anomalyKnowledge);

    public static Exception? Finalizer(Exception? __exception, object? __state, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge)
    {
        ResearchProbe.End(__state as ResearchProbe.Scope, ___progress, ___anomalyKnowledge);
        return __exception;
    }
}

[MetricAdapter(ResearchFamily.Name)]
[HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
public static class ResearchFinishProjectMetricPatch
{
    public static void Prefix(ResearchProjectDef proj, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge, out bool? __state)
    {
        __state = null;
        if (proj is null || LiveMetrics.Family(ResearchProbe.Unit) is not { } s)
            return;
        try
        {
            __state = ResearchProbe.Hooks.FinishBegin(s, proj.defName, ResearchProbe.Finished(___progress, ___anomalyKnowledge, proj),
                ResearchProbe.Progress(___progress, ___anomalyKnowledge, proj), Find.TickManager.TicksGame);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(ResearchProbe.Unit, e);
        }
    }

    public static void Postfix(ResearchProjectDef proj, Dictionary<ResearchProjectDef, float> ___progress,
        Dictionary<ResearchProjectDef, float> ___anomalyKnowledge, bool? __state)
    {
        if (__state is not { } wasFinished || proj is null)
            return;
        try
        {
            ResearchProbe.Hooks.FinishEnd(LiveMetrics.Family(ResearchProbe.Unit), proj.defName, wasFinished,
                ResearchProbe.Finished(___progress, ___anomalyKnowledge, proj), Find.TickManager.TicksGame);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(ResearchProbe.Unit, e);
        }
    }
}
