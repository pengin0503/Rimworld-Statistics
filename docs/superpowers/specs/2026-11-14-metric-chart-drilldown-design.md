# Metric drill-down charts (2026-11-14)

## Intent

Keep **RimWorld Statistics** numeric-first, but let players inspect the existing
statistics visually without leaving the Details tab or importing saves again.
Clicking a top-level Details metric row, a supported Overview card, or a
favourite metric card opens a separate chart dialog. The favourite star and
Details disclosure triangle remain independent click targets.

## Alternatives considered

1. **Replace the numeric table with charts** — rejected: several hundred metrics
   would increase drawing cost and make individual numbers harder to browse.
2. **Always-on small chart beside every statistic** — rejected: high redraw cost,
   poor readability, and unsuitable for metrics with no comparable history.
3. **On-demand drill-down dialog (adopted)** — a chart is computed only for the
   selected key, using the already-cached `NumericView`. No persistence change,
   save rescan, or extra game tick hooks are needed.

## Data semantics

- The default chart compares a **single recorded numeric value for each included
  colony**, using the same precomputed per-colony result shown by Details.
  Overview's basic lifetime metrics use their existing `BasicMetrics.Value`
  projections, not newly invented observation data.
- Where a metric has recorded breakdown rows, the dialog can instead show
  category/component values. From per-colony mode it initially shows that
  colony's breakdown, when available.
- If only an overall value exists, a labelled single-bar chart is used.
- A metric with no measured numeric value displays an explicit no-data message.
- Missing, unknown, non-applicable, and non-finite values are **not** converted to
  zero. Real zeroes, negatives, and partial-coverage values remain visible.
- Excluded/out-of-scope colonies do not enter a cross-colony chart; an explicitly
  selected colony can still show its own breakdown.
- Bars are sorted by value descending. The zero baseline accommodates positive
  and negative data. Partial readings use a muted bar colour.
- Chart copy explicitly states these are **not time-series graphs**.

## Why not a time series in this version?

Many current outputs are maxima/minima, lifetime aggregates, rates, rankings, or
post-compaction snapshots. Existing `MetricObservation` objects do not imply that
all saved observations are points on one continuous lineage. Plotting them as a
continuous trend would conflate forks and different observation semantics.
Accurate history charts require metric-specific selection, lineage-aware samples
and gap/coverage labelling. They are intentionally deferred rather than invented.

## UI and performance

- Only the visible chart rows are rendered in a scroll view.
- The data series and graph ranges are computed once per dialog.
- Chart window size is clamped to the current screen.
- There are no changes to JSON schema, capture hooks, existing values, or
  the default Details screen arrangement.
- Chart UI strings are included in all 12 currently supported languages.

## Verification

Unit tests specify scope/exclusion filtering, real zero and negative values,
partial readings, basic Overview values, per-colony breakdown selection, and
absence of fabricated aggregate values. GitHub Actions builds the RimWorld assembly, runs Core tests,
and checks translation consistency. Actual game UI validation is separate.
