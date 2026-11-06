using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Stamps a campaign id and the lineage of endings into the save (spec §8.1) and records live observations (§8.2, §8.3).
/// Never ticks. Every hook catches its own exceptions so saving and loading always continue.
/// The full type name must equal <see cref="SaveReader.ComponentClassName"/>.
/// </summary>
public sealed class GameComponent_RimworldStatistics : GameComponent
{
    public string? campaignId;
    public List<string> lineageTokens = new();
    public int dataVersion = 1;

    public GameComponent_RimworldStatistics(Game game)
    {
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
            ProfileService.Session.Mutate(p => ProfileMutations.RegisterLiveCampaign(p, id, identity, display, DateTime.UtcNow), SaveMode.Immediate);
        }
        catch (Exception e)
        {
            Fail("StartedNewGame", e);
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
            session.Mutate(p =>
            {
                ProfileMutations.RegisterLiveCampaign(p, id, identity, display, DateTime.UtcNow);
                foreach (var ev in seeded)
                    ProfileMutations.UpsertEnding(p, ev);
            }, SaveMode.Immediate);
        }
        catch (Exception e)
        {
            Fail("LoadedGame", e);
        }
    }

    public override void FinalizeInit()
    {
        try
        {
            ImportRunner.Cancel();   // a game is starting: stop importing; the rest is read on the next return to the title
        }
        catch (Exception e)
        {
            Fail("FinalizeInit", e);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref campaignId, "campaignId");
        Scribe_Collections.Look(ref lineageTokens, "lineageEndings", LookMode.Value);
        Scribe_Values.Look(ref dataVersion, "dataVersion", 1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && lineageTokens == null)
            lineageTokens = new List<string>();
        if (Scribe.mode == LoadSaveMode.Saving)
            RecordSave();
    }

    /// <summary>Spec §8.3: on every save, refresh the live record; the file is written in the background.</summary>
    private void RecordSave()
    {
        try
        {
            if (string.IsNullOrEmpty(campaignId))
                campaignId = NewId();   // only if an earlier hook failed; the id is still stamped into this save
            var id = campaignId!;
            var identity = LiveStateReader.ReadIdentity(id);
            var values = LiveStateReader.ReadValues();
            var display = LiveStateReader.ReadDisplay();
            ProfileService.Session.Mutate(p => ProfileMutations.ApplyLiveObservation(p, id, identity, values, display, DateTime.UtcNow), SaveMode.Background);
        }
        catch (Exception e)
        {
            Fail("ExposeData (saving)", e);
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void Fail(string where, Exception e)
        => Log.ErrorOnce($"[RimworldStatistics] {where} failed; the game continues without this record.\n{e}", ("RWS.gc." + where).GetHashCode());
}
