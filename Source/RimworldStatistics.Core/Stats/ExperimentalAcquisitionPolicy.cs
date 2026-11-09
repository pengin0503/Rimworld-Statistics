using System.Collections.Frozen;

namespace RimworldStatistics.Core;

/// <summary>User intent. A new unit is never opted in by upgrading the mod.</summary>
public sealed record ExperimentalSelection(bool Enabled, IReadOnlyCollection<string> ExplicitUnitIds);

/// <summary>Resolves acquisition permission without changing verification evidence or using game types.</summary>
public static class ExperimentalAcquisitionPolicy
{
    public static IReadOnlySet<string> Resolve(ExperimentalSelection selection, IReadOnlyCollection<AcquisitionUnit> catalog)
    {
        var explicitlySelected = selection.Enabled
            ? new HashSet<string>(selection.ExplicitUnitIds ?? Array.Empty<string>(), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        return catalog.Where(u => u.Kind != AcquisitionKind.Derived
                && (u.Status == AcquisitionStatus.Verified || explicitlySelected.Contains(u.Id)))
            .Select(u => u.Id).ToFrozenSet(StringComparer.Ordinal);
    }
}
