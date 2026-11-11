namespace RimworldStatistics.HospitalityAddon;

/// <summary>Physical beds assigned to current Hospitality guests, not sleepers or available slots.</summary>
internal sealed class HospitalityAssignedBedCounter
{
    private readonly HashSet<object> beds = new(ReferenceEqualityComparer.Instance);
    private bool homeMapObserved;
    private bool invalid;

    internal void MarkObservedMap() => homeMapObserved = true;

    internal bool Add(object? bed)
    {
        if (invalid) return false;
        if (bed is null)
        {
            invalid = true;
            return false;
        }
        beds.Add(bed);
        return true;
    }

    internal double? Value => !homeMapObserved || invalid ? null : beds.Count;
}
