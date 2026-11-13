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
}
