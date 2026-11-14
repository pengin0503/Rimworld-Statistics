using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Stamps a campaign id and the lineage of endings into the save (spec §8.1) and records live observations (§8.2, §8.3).
/// Never ticks; each frame it only reports failures of profile changes applied off the game thread. Every hook catches its
/// own exceptions so saving and loading always continue.
/// The full type name must equal <see cref="SaveReader.ComponentClassName"/>.
/// </summary>
public sealed class GameComponent_RimworldStatistics : GameComponent
{
    public string? campaignId;
    public List<string> lineageTokens = new();
    public int dataVersion = 1;

    /// <summary>
    /// The checkpoint text of the numeric statistics (Phase C): as loaded, then what each save writes. Data this version
    /// cannot read, or written while no family records, passes through unchanged (numeric expansion design §3 step 5).
    /// </summary>
    public string? numericStatistics;

    /// <summary>The text as the save held it: written back when no checkpoint scope runs (never an earlier save's draft).</summary>
    private string? _loadedNumericStatistics;

    private readonly Game _game;

    public GameComponent_RimworldStatistics(Game game)
    {
        _game = game;
    }

    public static GameComponent_RimworldStatistics? CurrentOrNull => Current.Game?.GetComponent<GameComponent_RimworldStatistics>();

    /// <summary>
    /// The lineage parsed from the tokens. Setting it writes the entries back; tokens this version cannot parse
    /// (written by a newer version) are kept.
    /// </summary>
    public List<LineageEntry> Lineage
    {
        get
        {
            var list = new List<LineageEntry>();
            foreach (var token in lineageTokens)
                if (LineageEntry.TryParse(token, out var entry))
                    list.Add(entry);
            return list;
        }
        set
        {
            var unknown = lineageTokens.Where(t => !LineageEntry.TryParse(t, out _)).ToList();
            lineageTokens = value.Select(e => e.ToToken()).Concat(unknown).ToList();
        }
    }

    public override void StartedNewGame()
    {
        try
        {
            campaignId = NewId();
            lineageTokens = new List<string>();
            EndingRuntime.Context.Reset();
            var id = campaignId;
            var identity = LiveStateReader.ReadIdentity(id);
            var display = LiveStateReader.ReadDisplay();
            ProfileService.Session.Mutate(p => ProfileMutations.RegisterLiveCampaign(p, id, identity, display), SaveMode.Immediate);
        }
        catch (Exception e)
        {
            Fail("StartedNewGame", e);
        }
        try
        {
            numericStatistics = null;
            _loadedNumericStatistics = null;
            LiveMetrics.StartNew(_game);
        }
        catch (Exception e)
        {
            Fail("StartedNewGame.numeric", e);
        }
    }

    public override void LoadedGame()
    {
        try
        {
            EndingRuntime.Context.Reset();
            var session = ProfileService.Session;
            if (string.IsNullOrEmpty(campaignId))
            {
                // A save from before the mod (spec §8.2): reuse the id of the same colony if one was registered, so
                // loading pre-install saves of one colony again and again does not create new colonies.
                var probe = LiveStateReader.ReadIdentity(null);
                campaignId = session.Read(p => CampaignMatcher.FindCampaignIdFor(p, probe)) ?? NewId();
            }
            var id = campaignId!;
            var identity = LiveStateReader.ReadIdentity(id);
            var display = LiveStateReader.ReadDisplay();
            var lineage = Lineage;
            var traces = EndingTraces.Detect(LiveStateReader.ReadTraceInputs(lineage.Select(e => e.Type).Distinct().ToList()));
            var seeded = EndingLedger.SeedTraces(id, lineage, traces, Find.TickManager.TicksGame,
                Find.StoryWatcher.statsRecord.colonistsLaunched, LiveStateReader.GameVersion, DateTime.UtcNow);
            if (seeded.Count > 0)
                Lineage = lineage;
            var recovered = ResumeNumeric(identity);
            session.Mutate(p =>
            {
                ProfileMutations.RegisterLiveCampaign(p, id, identity, display);
                foreach (var ev in seeded)
                    ProfileMutations.UpsertEnding(p, ev);
                if (recovered != null)
                    ProfileMutations.UpsertMetricObservation(p, recovered);   // idempotent: same id, same content
            }, SaveMode.Immediate);
        }
        catch (Exception e)
        {
            Fail("LoadedGame", e);
        }
    }

    /// <summary>
    /// Restores the numeric totals from the loaded checkpoint and returns that checkpoint's observation, so a save written
    /// successfully but never confirmed (its postfix did not run) is recovered with the v1 registration of this load.
    /// </summary>
    private MetricObservation? ResumeNumeric(ColonyIdentity identity)
    {
        try
        {
            var acc = LiveMetrics.Resume(_game, numericStatistics);
            return acc.Loaded is { } loaded && acc.LoadedJson is { } json
                ? CheckpointMetrics.ToObservation(loaded, json, identity, LiveStateReader.GameVersion, LiveMetrics.ActiveDlcIds())
                : null;
        }
        catch (Exception e)
        {
            Fail("LoadedGame.numeric", e);
            return null;
        }
    }

    public override void FinalizeInit()
    {
        try
        {
            ImportRunner.CancelForGameStart();   // stop importing; the rest is read on the next return to the title screen
        }
        catch (Exception e)
        {
            Fail("FinalizeInit", e);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.Saving)
            EnsureCampaignId();
        Scribe_Values.Look(ref campaignId, "campaignId");
        Scribe_Collections.Look(ref lineageTokens, "lineageEndings", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.Saving)
            PrepareNumericForSave();
        Scribe_Values.Look(ref dataVersion, "dataVersion", 1);
        Scribe_Values.Look(ref numericStatistics, "numericStatistics");
        if (Scribe.mode == LoadSaveMode.LoadingVars)
            _loadedNumericStatistics = numericStatistics;
        if (Scribe.mode == LoadSaveMode.PostLoadInit && lineageTokens == null)
            lineageTokens = new List<string>();
    }

    /// <summary>The text of this save: the scope's checkpoint, or (no scope, another game, an error) what was loaded, unchanged.</summary>
    private void PrepareNumericForSave()
    {
        try
        {
            // With a scope its text is the answer, null included (nothing committed yet). Without one, what the save held.
            numericStatistics = ReferenceEquals(Current.Game, _game) && CheckpointSavePatch.TryGetText(out var text) ? text : _loadedNumericStatistics;
            if (numericStatistics != null)
                dataVersion = Math.Max(dataVersion, 2);
        }
        catch (Exception e)
        {
            Fail("ExposeData.numeric", e);
        }
    }

    /// <summary>
    /// Spec §8.3: on every save, refresh the live record; the file is written in the background. A committed checkpoint of the
    /// same save is recorded in the same profile change (numeric expansion design §2 "同じ成功入力でv1記録と拡張観測を一度のロックで反映").
    /// </summary>
    internal void RecordSave(string path, SaveDraft? checkpoint)
    {
        try
        {
            var id = EnsureCampaignId();
            var identity = LiveStateReader.ReadIdentity(id);
            var values = LiveStateReader.ReadValues();
            var display = LiveStateReader.ReadDisplay();
            var version = LiveStateReader.GameVersion;
            var file = new FileInfo(path);
            if (!file.Exists)
                return;
            var savedUtc = file.LastWriteTimeUtc;
            var numeric = NumericObservation(checkpoint, identity, version);
            // Read detached snapshots only after the game file has successfully committed.
            var snapshot = SavedStateMetricReader.Read(identity, version, savedUtc);
            // Both come from this one successful save: the checkpoint names the snapshot's place in the save lineage,
            // so a history chart can join it only to snapshots of proven ancestor saves.
            if (snapshot != null && numeric != null && numeric.SaveTick == snapshot.SaveTick)
                snapshot.CheckpointKey = numeric.Key;
            var providers = ModProviderAdapters.ActiveDescriptors;   // read here, on the game thread, for this save
            // The game thread never waits for the profile (Issue #195): while a background write or an import holds it,
            // this change is applied right after, in order, before anything else reads or writes the profile.
            ProfileService.Session.MutateWithoutBlocking(p =>
            {
                try
                {
                    ProfileMutations.ApplyLiveObservation(p, id, identity, values, display, savedUtc, version);
                    // Record the interpretation before snapshot compaction sees the new keys.
                    if (providers.Count > 0 && (numeric != null || snapshot != null))
                        ProfileMutations.RememberModMetrics(p, providers);
                    if (numeric != null)
                        ProfileMutations.UpsertMetricObservation(p, numeric);
                    if (snapshot != null)
                        ProfileMutations.UpsertMetricObservation(p, snapshot);
                }
                catch (Exception e)
                {
                    DeferredFailures.Enqueue(e);   // this may run on a worker thread: report it from the game thread
                }
            });
        }
        catch (Exception e)
        {
            Fail("RecordSave", e);
        }
    }

    // Failures of RecordSave changes the profile applied after RecordSave returned. Log.Error can pause the game, so they
    // are reported from the game thread.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Exception> DeferredFailures = new();

    public override void GameComponentUpdate()
    {
        while (DeferredFailures.TryDequeue(out var e))
            Fail("RecordSave", e);
    }

    private static MetricObservation? NumericObservation(SaveDraft? checkpoint, ColonyIdentity identity, string version)
    {
        if (checkpoint?.Payload is not { } payload)
            return null;
        try
        {
            return CheckpointMetrics.ToObservation(payload, checkpoint.Json, identity, version, LiveMetrics.ActiveDlcIds());
        }
        catch (Exception e)
        {
            Fail("RecordSave.numeric", e);   // the save and its v1 record are unaffected; the file keeps the checkpoint for import
            return null;
        }
    }

    /// <summary>The campaign id; a new one if an earlier hook failed (it is still stamped into the next save).</summary>
    public string EnsureCampaignId()
    {
        if (string.IsNullOrEmpty(campaignId))
            campaignId = NewId();
        return campaignId!;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void Fail(string where, Exception e)
        => Log.ErrorOnce($"[RimworldStatistics] {where} failed; the game continues without this record.\n{e}", ("RWS.gc." + where).GetHashCode());
}
