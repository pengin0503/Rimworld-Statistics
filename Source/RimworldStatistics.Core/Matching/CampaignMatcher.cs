namespace RimworldStatistics.Core;

/// <summary>Groups observations and live records into colonies (spec §7.4) with stable keys (spec §6.2).</summary>
public static class CampaignMatcher
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>Same fingerprint and starting pawn sets in a subset relation (either way).</summary>
    public static bool SameColony(ColonyIdentity a, ColonyIdentity b)
    {
        if (!a.HasFingerprint || !b.HasFingerprint)
            return false;
        if (a.FingerprintHash() != b.FingerprintHash())
            return false;
        return SubsetEitherWay(new HashSet<string>(a.StartingPawnIds, Ord), new HashSet<string>(b.StartingPawnIds, Ord));
    }

    public static string? FindCampaignIdFor(Profile profile, ColonyIdentity identity)
        => profile.LiveCampaigns
            .Where(c => SameColony(c.Identity, identity))
            .Select(c => c.CampaignId)
            .OrderBy(id => id, Ord)
            .FirstOrDefault();

    /// <summary>Rebuilds every colony from the profile. May append to <see cref="Profile.ColonyKeys"/>.</summary>
    public static IReadOnlyList<Colony> BuildColonies(Profile profile)
    {
        var colonies = GroupByFingerprint(GroupById(profile));
        AssignKeys(profile, colonies);
        AttachEndings(profile, colonies);
        ApplyManualMerges(profile, colonies);
        foreach (var c in colonies)
        {
            foreach (var o in c.Observations)
            {
                c.Traces.UnionWith(o.EndingTraces);
                // Spec §4.3 drops traces the save's lineage explains so recorded endings are not counted twice; when the
                // profile has no such record (lost, or restored from an older backup), the trace is the remaining evidence.
                foreach (var t in o.ExplainedTraces)
                    if (!c.Endings.Any(e => EndingTraces.Explains(e.Type, t)))
                        c.Traces.Add(t);
            }
            c.Excluded = profile.UserDecisions.ExcludedCampaignKeys.Any(k => k == c.Key || c.AliasKeys.Contains(k, Ord));
        }
        return colonies;
    }

    private sealed class Group
    {
        public List<SaveObservation> Obs { get; } = new();
        public List<LiveCampaign> Live { get; } = new();
        public HashSet<string> Pawns { get; } = new(Ord);
        public string? Hash { get; set; }

        public DateTime Oldest => Obs.Select(o => o.LastWriteUtc).Concat(Live.Select(l => l.LastSavedUtc)).DefaultIfEmpty(DateTime.MaxValue).Min();
        public string MinFile => Obs.Select(o => o.FileName).DefaultIfEmpty("").Min(Ord)!;
        public string MinId => Live.Select(l => l.CampaignId).Concat(Obs.Select(o => o.Identity.ModCampaignId ?? "")).DefaultIfEmpty("").Min(Ord)!;
        public string PawnText => string.Join(",", Pawns.OrderBy(p => p, Ord));

        public IEnumerable<ColonyIdentity> Identities => Live.Select(l => l.Identity).Concat(Obs.Select(o => o.Identity));
    }

    /// <summary>Steps 1–2: one unit per live record and per usable observation; units with the same campaign id form one group.</summary>
    private static List<Group> GroupById(Profile profile)
    {
        var groups = new List<Group>();
        var byId = new Dictionary<string, Group>(Ord);
        Group For(string? id)
        {
            if (id is null)
            {
                var solo = new Group();
                groups.Add(solo);
                return solo;
            }
            if (!byId.TryGetValue(id, out var g))
            {
                g = new Group();
                byId[id] = g;
                groups.Add(g);
            }
            return g;
        }

        foreach (var live in profile.LiveCampaigns)
            For(live.CampaignId).Live.Add(live);
        foreach (var obs in profile.SaveObservations)
        {
            if (obs.Status == ObservationStatus.Error)
                continue;
            if (!obs.Identity.HasFingerprint && obs.Identity.ModCampaignId is null)
                continue;
            For(obs.Identity.ModCampaignId).Obs.Add(obs);
        }

        foreach (var g in groups)
        {
            foreach (var id in g.Identities)
                g.Pawns.UnionWith(id.StartingPawnIds);
            // A group's fingerprint: that of its first unit (in a fixed order) that has one.
            g.Hash = g.Live.OrderBy(l => l.CampaignId, Ord).Select(l => l.Identity)
                .Concat(g.Obs.OrderBy(o => o.FileName, Ord).ThenBy(o => o.LastWriteUtc).ThenBy(o => o.FileSize).Select(o => o.Identity))
                .Select(i => i.FingerprintHash()).FirstOrDefault(h => h is not null);
        }
        return groups;
    }

    /// <summary>Steps 3–4: in a fixed order (most starting pawns first, so ancestors lead), join groups by fingerprint and pawn subset.</summary>
    private static List<Colony> GroupByFingerprint(List<Group> groups)
    {
        var ordered = groups
            .OrderByDescending(g => g.Pawns.Count)
            .ThenBy(g => g.Oldest)
            .ThenBy(g => g.MinFile, Ord)
            .ThenBy(g => g.MinId, Ord)
            .ThenBy(g => g.Hash ?? "", Ord)
            .ThenBy(g => g.PawnText, Ord);

        var colonies = new List<Colony>();
        foreach (var g in ordered)
        {
            var target = g.Hash is null
                ? null
                : colonies.FirstOrDefault(c => c.FingerprintHash == g.Hash && SubsetEitherWay(c.StartingPawnUnion, g.Pawns));
            if (target is null)
            {
                target = new Colony { FingerprintHash = g.Hash };
                colonies.Add(target);
            }
            target.Observations.AddRange(g.Obs);
            target.LiveCampaigns.AddRange(g.Live);
            target.StartingPawnUnion.UnionWith(g.Pawns);
        }
        return colonies;
    }

    /// <summary>
    /// Step 6: fp:&lt;hash&gt; keys, numbered through the persisted ledger when fingerprints collide.
    /// Every key minted here is recorded in the ledger, including a lone colony's plain fp:&lt;hash&gt;: otherwise a
    /// colliding colony found later that sorts first would take fp:&lt;hash&gt; and renumber the original colony,
    /// detaching its user decisions (spec §6.2: the first colony keeps fp:&lt;hash&gt;; keys never change).
    /// </summary>
    private static void AssignKeys(Profile profile, List<Colony> colonies)
    {
        var claimed = new HashSet<ColonyKeyEntry>();
        var usedKeys = new HashSet<string>(Ord);
        foreach (var c in colonies)
        {
            if (c.FingerprintHash is not { } hash)
            {
                c.Key = "id:" + c.CampaignIds().OrderBy(i => i, Ord).First();
                continue;
            }
            // A live campaign is keyed id:<campaignId> until its first wealth record completes the fingerprint; keep
            // those keys as aliases so a decision made in the meantime still applies after the key becomes fp:<hash>.
            c.AliasKeys.AddRange(c.CampaignIds().OrderBy(i => i, Ord).Select(i => "id:" + i));
            var entries = profile.ColonyKeys.Where(e => e.FingerprintHash == hash).ToList();
            var matches = entries
                .Where(e => !claimed.Contains(e) && SubsetEitherWay(new HashSet<string>(e.StartingPawnIds, Ord), c.StartingPawnUnion))
                .OrderBy(e => KeyNumber(e.Key))
                .ToList();
            if (matches.Count > 0)
            {
                c.Key = matches[0].Key;
                c.AliasKeys.AddRange(matches.Skip(1).Select(e => e.Key));
                foreach (var e in matches)
                {
                    claimed.Add(e);
                    e.StartingPawnIds = e.StartingPawnIds.Union(c.StartingPawnUnion, Ord).OrderBy(p => p, Ord).ToList();
                }
                usedKeys.Add(c.Key);
                continue;
            }
            var plain = "fp:" + hash;
            var taken = entries.Select(e => e.Key).Concat(usedKeys.Where(k => k.StartsWith(plain, StringComparison.Ordinal))).ToList();
            var key = taken.Contains(plain, Ord) ? $"{plain}#{Math.Max(2, taken.Select(KeyNumber).Max() + 1)}" : plain;
            c.Key = key;
            usedKeys.Add(key);
            var entry = new ColonyKeyEntry { Key = key, FingerprintHash = hash, StartingPawnIds = c.StartingPawnUnion.OrderBy(p => p, Ord).ToList() };
            profile.ColonyKeys.Add(entry);
            claimed.Add(entry);
        }
    }

    /// <summary>"fp:h" is 1, "fp:h#n" is n.</summary>
    private static int KeyNumber(string key)
    {
        var hashMark = key.LastIndexOf('#');
        return hashMark >= 0 && int.TryParse(key.AsSpan(hashMark + 1), out var n) ? n : 1;
    }

    /// <summary>Step 7: endings go to the colony holding their campaign id; an unknown id gets its own colony.</summary>
    private static void AttachEndings(Profile profile, List<Colony> colonies)
    {
        var byId = new Dictionary<string, Colony>(Ord);
        foreach (var c in colonies)
            foreach (var id in c.CampaignIds())
                byId.TryAdd(id, c);
        foreach (var e in profile.EndingEvents)
        {
            if (!byId.TryGetValue(e.CampaignId, out var c))
            {
                c = new Colony { Key = "id:" + e.CampaignId };
                colonies.Add(c);
                byId[e.CampaignId] = c;
            }
            c.Endings.Add(e);
        }
    }

    /// <summary>Step 5 (applied last, spec §7.4): each manual merge list joins the colonies it names into the first of them.</summary>
    private static void ApplyManualMerges(Profile profile, List<Colony> colonies)
    {
        foreach (var list in profile.UserDecisions.ManualMerges)
        {
            var named = list
                .Select(k => colonies.FirstOrDefault(c => c.Key == k || c.AliasKeys.Contains(k, Ord)))
                .OfType<Colony>()
                .Distinct()
                .ToList();
            if (named.Count < 2)
                continue;
            var target = named[0];
            foreach (var other in named.Skip(1))
            {
                target.Observations.AddRange(other.Observations);
                target.LiveCampaigns.AddRange(other.LiveCampaigns);
                target.Endings.AddRange(other.Endings);
                target.StartingPawnUnion.UnionWith(other.StartingPawnUnion);
                foreach (var k in other.AliasKeys.Prepend(other.Key))
                    if (k != target.Key && !target.AliasKeys.Contains(k, Ord))
                        target.AliasKeys.Add(k);
                colonies.Remove(other);
            }
        }
    }

    private static bool SubsetEitherWay(HashSet<string> a, HashSet<string> b) => a.IsSubsetOf(b) || b.IsSubsetOf(a);
}
