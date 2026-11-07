using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>Reads what the main mod recorded so far in the running game, without changing it.</summary>
internal static class VerifyProbe
{
    public static string GameVersion => VersionControl.CurrentVersionStringWithRev;

    /// <summary>
    /// The values the next successful save would record, from a detached preview of the running totals (the same
    /// PrepareSave/ToObservation path as a save, without committing anything). Empty with a reason when nothing records.
    /// </summary>
    public static SortedDictionary<string, FlatValue> Values(out string? why)
    {
        why = null;
        var acc = LiveMetrics.Accumulator;
        if (acc is null)
        {
            why = "no game is being recorded (no game, another game, or still setting up)";
            return new SortedDictionary<string, FlatValue>(StringComparer.Ordinal);
        }
        var draft = acc.PrepareSave(Find.TickManager.TicksGame, DateTime.UtcNow, "verify-preview");
        if (draft?.Payload is null)
        {
            why = "no family records in this game";
            return new SortedDictionary<string, FlatValue>(StringComparer.Ordinal);
        }
        var obs = CheckpointMetrics.ToObservation(draft.Payload, draft.Json, new ColonyIdentity(), GameVersion, LiveMetrics.ActiveDlcIds());
        return VerifyValues.Flatten(obs);
    }

    public static int Hits(string unit) => LiveMetrics.HitCounts().GetValueOrDefault(unit);

    /// <summary>The SHA-256 of the running Assembly-CSharp.dll (the evidence document asks for it with every run).</summary>
    public static string GameAssemblyHash()
    {
        try
        {
            var path = typeof(Pawn).Assembly.Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "unknown";
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch (Exception e)
        {
            return "unknown (" + e.GetType().Name + ")";
        }
    }

    /// <summary>The game methods the main mod's Harmony instance patched, with the kinds of patch on each.</summary>
    public static List<string> MainModPatches()
    {
        var result = new List<string>();
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(method);
            if (info is null || !info.Owners.Contains(RimworldStatisticsMod.HarmonyId))
                continue;
            string Kinds()
            {
                var k = new List<string>();
                if (info.Prefixes.Any(p => p.owner == RimworldStatisticsMod.HarmonyId)) k.Add("prefix");
                if (info.Postfixes.Any(p => p.owner == RimworldStatisticsMod.HarmonyId)) k.Add("postfix");
                if (info.Finalizers.Any(p => p.owner == RimworldStatisticsMod.HarmonyId)) k.Add("finalizer");
                if (info.Transpilers.Any(p => p.owner == RimworldStatisticsMod.HarmonyId)) k.Add("transpiler");
                return string.Join("+", k);
            }
            result.Add($"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name))}) {Kinds()}");
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>The status report: version, hashes, DLCs, every unit's gate, the running families and the checkpoint parent.</summary>
    public static string Status()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{CaseCheck.LogPrefix} status game={GameVersion} assembly-csharp-sha256={GameAssemblyHash()} harmony={typeof(Harmony).Assembly.GetName().Version}");
        sb.AppendLine($"  save data folder: {GenFilePaths.SaveDataFolderPath}");
        sb.AppendLine($"  active DLCs: {string.Join(",", LiveMetrics.ActiveDlcIds())}");
        sb.AppendLine($"  harness opened: {(VerifyHarnessMod.Opened.Count == 0 ? "nothing (" + (VerifyHarnessMod.Declined ?? "no unit requested") + ")" : string.Join(",", VerifyHarnessMod.Opened))}");
        sb.AppendLine($"  checkpoint save patch applied: {MetricAdapters.CheckpointApplied}");
        var opened = new HashSet<string>(VerifyHarnessMod.Opened, StringComparer.Ordinal);
        foreach (var u in AcquisitionUnits.All)
        {
            var open = MetricGates.IsOpen(u.Id);
            var recording = MetricGates.IsRecording(u.Id);
            var dlcOff = u.RequiredDlc != null && !ModsConfig.IsActive(u.RequiredDlc);
            var state = recording ? "recording" : open ? "open, not applied" : dlcOff ? "closed (DLC off)"
                : opened.Contains(u.Id) || u.Status == AcquisitionStatus.Verified ? "closed (failed this session)" : "closed";
            sb.AppendLine($"  unit {u.Id} kind={u.Kind} status={u.Status} {state} hits={Hits(u.Id)}");
        }
        if (LiveMetrics.Accumulator is { } acc)
        {
            sb.AppendLine($"  checkpoint parent: {acc.ParentId ?? "(none)"}");
            foreach (var name in acc.Recording.OrderBy(n => n, StringComparer.Ordinal))
                if (acc.Family(name) is { } f)
                    sb.AppendLine($"  family {name} revision={f.Revision} since={f.SinceTick} through={f.ThroughTick} continuous={f.Continuous} accumulators={f.Metrics.Count} trackers={f.Active.Count}");
        }
        else
        {
            sb.AppendLine("  no game is being recorded");
        }
        var patches = MainModPatches();
        sb.AppendLine($"  methods patched by {RimworldStatisticsMod.HarmonyId}: {patches.Count}");
        foreach (var p in patches)
            sb.AppendLine("    " + p);
        return sb.ToString().TrimEnd();
    }

    /// <summary>The checkpoint observations the profile holds for this game's campaign, and the selection the Details tab uses.</summary>
    public static string Lineage()
    {
        var sb = new StringBuilder();
        var campaign = GameComponent_RimworldStatistics.CurrentOrNull?.campaignId;
        sb.AppendLine($"{CaseCheck.LogPrefix} lineage campaign={campaign ?? "(none)"} parent-in-game={LiveMetrics.Accumulator?.ParentId ?? "(none)"}");
        if (campaign is null)
            return sb.ToString().TrimEnd();
        if (ProfileService.TryGetSession() is not { } session)
        {
            sb.AppendLine("  the profile is still loading; try again");
            return sb.ToString().TrimEnd();
        }
        // Everything is read under the session's lock: the profile may change on its background thread.
        var lines = session.Read(p =>
        {
            var nodes = p.MetricObservations
                .Where(o => o.Source == MetricObservationSource.Checkpoint && o.Identity.ModCampaignId == campaign).ToList();
            var text = new List<string>();
            foreach (var n in nodes.OrderBy(n => n.SaveTick).ThenBy(n => n.Key, StringComparer.Ordinal))
            {
                text.Add($"  checkpoint {n.Key} parent={n.ParentKey ?? "(root)"} tick={n.SaveTick} utc={n.ObservedUtc:O} conflicted={n.Conflicted} compressed={n.Compressed} metrics={n.Metrics.Count}");
                foreach (var (key, v) in VerifyValues.Flatten(n))
                    text.Add($"    {key} = {v.Display}");
            }
            var selection = CheckpointSelector.Select(nodes);
            text.Add($"  representative={selection.Representative?.Key ?? "(none)"} lineage={selection.Lineage} checkpoints={selection.Checkpoints}");
            return text;
        });
        foreach (var line in lines)
            sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }
}
