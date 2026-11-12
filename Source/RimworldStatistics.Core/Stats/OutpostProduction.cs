namespace RimworldStatistics.Core;

public static class OutpostProduction
{
    public const string Unit = "outpost_production";
    public const string Deliveries = "exploration.outposts.production_deliveries";
    public const string ByDef = "exploration.outposts.produced_units_by_def";
    private const string BatchCount = "batches";
    private const string Products = "products";

    public static LiveFamily Family { get; } = new() { Name = Unit, Revision = 1, Build = Build };

    public static Delivery<T> Capture<T>(IEnumerable<T> products, Func<T, (string? DefName, int Count)> describe)
    {
        var items = new List<T>();
        var units = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var product in products)
        {
            items.Add(product);
            var (name, count) = describe(product);
            if (!string.IsNullOrWhiteSpace(name) && count > 0)
                units[name] = units.GetValueOrDefault(name) + count;
        }
        return new Delivery<T>(items, units);
    }

    public sealed class Delivery<T>(IReadOnlyList<T> items, IReadOnlyDictionary<string, long> units)
    {
        private bool recorded;
        public IReadOnlyList<T> Items { get; } = items;

        public void Record(FamilyState state, bool succeeded)
        {
            if (!succeeded || recorded || units.Count == 0)
                return;
            recorded = true;
            state.Count(BatchCount);
            foreach (var (key, value) in units)
            {
                state.Count(Products, value);
                state.CountBy(Products, key, value);
            }
        }
    }

    private static void Build(FamilyOutputContext output)
    {
        output.PutCount(BatchCount, Deliveries);
        var products = output.State.Peek(Products);
        output.PutBreakdown(ByDef, output.Counts(products), output.Value(integer: products?.N ?? 0, partial: products?.Overflow == true));
    }
}
