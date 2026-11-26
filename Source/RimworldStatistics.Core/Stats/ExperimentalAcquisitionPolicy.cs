using System.Collections.Frozen;

namespace RimworldStatistics.Core;

/// <summary>
/// The selected acquisition units for a running game. The all-units sentinel
/// allows new installations to record newly introduced units automatically;
/// an empty explicit set still means opt out.
/// </summary>
public sealed record ExperimentalSelection(bool Enabled, IReadOnlyCollection<string> ExplicitUnitIds);

/// <summary>Resolves acquisition permission without changing verification evidence or using game types.</summary>
public static class ExperimentalAcquisitionPolicy
{
    /// <summary>Persisted wildcard for automatically selecting current and future non-derived units.</summary>
    public const string AllUnitsToken = "*";

    public static IReadOnlySet<string> Resolve(ExperimentalSelection selection, IReadOnlyCollection<AcquisitionUnit> catalog)
    {
        var explicitlySelected = selection.Enabled
            ? new HashSet<string>(selection.ExplicitUnitIds ?? Array.Empty<string>(), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var autoAll = explicitlySelected.Contains(AllUnitsToken);
        return catalog.Where(u => u.Kind != AcquisitionKind.Derived
                && (u.Status == AcquisitionStatus.Verified || autoAll || explicitlySelected.Contains(u.Id)))
            .Select(u => u.Id).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// U1 "Recommended" preset: the units that can actually record in the current environment — not derived, the
    /// required DLC active (or none), and the owner MOD active (or none), materialized as concrete IDs rather than the
    /// wildcard so a later catalog addition is a deliberate opt-in. <paramref name="dlcActive"/> answers whether a DLC
    /// or package ID is loaded; <paramref name="modActive"/> answers the same for the unit as a whole, which lets the
    /// game resolve the unit's owner package (a unit with no owner passes true).
    /// </summary>
    public static IReadOnlyList<string> RecommendedSelection(
        IEnumerable<AcquisitionUnit> units,
        Func<string, bool> dlcActive,
        Func<string, bool> modActive)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(dlcActive);
        ArgumentNullException.ThrowIfNull(modActive);
        return units
            .Where(u => u.Kind != AcquisitionKind.Derived)
            .Where(u => u.RequiredDlc is not { } dlc || dlcActive(dlc))
            .Where(u => modActive(u.Id))
            .Select(u => u.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
