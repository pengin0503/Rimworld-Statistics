using System.Globalization;
using RimworldStatistics.Core;

namespace RimworldStatistics.VerifyHarness;

/// <summary>The units the harness opens, from the <c>-rwsverify=</c> command-line argument.</summary>
public static class VerifyArgs
{
    /// <summary>The command-line key: <c>-rwsverify=all</c> or <c>-rwsverify=research,mental</c>.</summary>
    public const string UnitsKey = "rwsverify";

    /// <summary>
    /// "all" (or no value) opens every acquisition unit; otherwise the comma-separated ids, in the order given, each once.
    /// Ids that are not acquisition units are returned separately so the harness can report them.
    /// </summary>
    public static (List<string> Units, List<string> Unknown) ParseUnits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            return (AcquisitionUnits.All.Select(u => u.Id).ToList(), new List<string>());
        var units = new List<string>();
        var unknown = new List<string>();
        foreach (var raw in value.Split(','))
        {
            var id = raw.Trim();
            if (id.Length == 0 || units.Contains(id, StringComparer.Ordinal) || unknown.Contains(id, StringComparer.Ordinal))
                continue;
            (AcquisitionUnits.Find(id) != null ? units : unknown).Add(id);
        }
        return (units, unknown);
    }
}

/// <summary>An expected change of one flat key: of its number, or (<see cref="Samples"/>) of its sample count.</summary>
public sealed record Expect(string Key, double Delta, bool Samples = false)
{
    public string Label => Key + (Samples ? "#n" : "");
}

public enum CaseOutcome
{
    Pass,
    Fail,

    /// <summary>The colony does not allow the case (no candidate pawn, project or faction): nothing was done.</summary>
    Skip,

    /// <summary>The case itself threw: its result says nothing about the mod.</summary>
    Error,

    /// <summary>A check inspected prerequisites only or lacks independent real-game ground truth.</summary>
    Inconclusive,
}

public sealed record CaseResult(string CaseId, string Unit, CaseOutcome Outcome, string Expected, string Actual, int Hooks, string? Note = null)
{
    /// <summary>Evidence provenance. GamePath means an existing real RimWorld action was invoked;
    /// other kinds must never be interpreted as a passed live-event acceptance test.</summary>
    public string Evidence { get; init; } = "GamePath";
}

/// <summary>Compares a case's expected changes with the two snapshots, and writes the harness's log line.</summary>
public static class CaseCheck
{
    public const string LogPrefix = "[RWS-VERIFY]";

    private static string Signed(double d)
        => (d >= 0 ? "+" : "") + d.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// PASS when every expected change happened exactly (numbers compared to 1e-9) and, when <paramref name="requireHooks"/>, the
    /// unit's hooks accepted at least one event. A negative case (every delta 0) passes without hooks: a hook that did not fire
    /// is a correct "not counted" too.
    /// </summary>
    public static CaseResult Evaluate(string caseId, string unit, IReadOnlyList<Expect> expected,
        IReadOnlyDictionary<string, FlatValue> before, IReadOnlyDictionary<string, FlatValue> after, int hooks, bool requireHooks, string? note = null)
    {
        var actual = expected.Select(e => e.Samples ? VerifyValues.SamplesDeltaOf(before, after, e.Key) : VerifyValues.DeltaOf(before, after, e.Key)).ToList();
        var ok = expected.Select((e, i) => Math.Abs(actual[i] - e.Delta) < 1e-9).All(x => x) && (!requireHooks || hooks > 0);
        return new CaseResult(caseId, unit, ok ? CaseOutcome.Pass : CaseOutcome.Fail,
            string.Join(",", expected.Select(e => e.Label + ":" + Signed(e.Delta))),
            string.Join(",", expected.Select((e, i) => e.Label + ":" + Signed(actual[i]))),
            hooks, note);
    }

    public static CaseResult Skip(string caseId, string unit, string why) => new(caseId, unit, CaseOutcome.Skip, "-", "-", 0, why);

    public static CaseResult Error(string caseId, string unit, Exception e) => new(caseId, unit, CaseOutcome.Error, "-", "-", 0, e.GetType().Name + ": " + e.Message);

    /// <summary>
    /// <c>[RWS-VERIFY] case=&lt;id&gt; unit=&lt;unit&gt; game=&lt;version&gt; expected=&lt;…&gt; actual=&lt;…&gt; hooks=&lt;n&gt; result=PASS|FAIL|SKIP|ERROR</c>
    /// and, when there is one, <c>note="…"</c> last. Fields hold no spaces except the quoted note.
    /// </summary>
    public static string Line(CaseResult r, string gameVersion)
    {
        var line = $"{LogPrefix} case={r.CaseId} unit={r.Unit} game={NoSpaces(gameVersion)} expected={r.Expected} actual={r.Actual} hooks={r.Hooks.ToString(CultureInfo.InvariantCulture)} result={r.Outcome.ToString().ToUpperInvariant()}";
        return r.Note is null ? line : line + " note=\"" + r.Note.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"";
    }

    private static string NoSpaces(string s) => s.Replace(' ', '_');
}
