namespace RimworldStatistics.Core.Tests;

public sealed class DetailsUiPolicyTests
{
    private static MetricDefinition Def(string key = "test.metric", string? acquisition = null, string? provider = null)
        => new()
        {
            Key = key,
            StoredKey = key,
            LedgerIds = key,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Provenance = MetricProvenance.SaveHistory,
            Overall = OverallAggregation.Sum,
            LabelKey = key,
            DescriptionKey = key,
            Acquisition = acquisition,
            ProviderId = provider,
        };

    [Fact]
    public void MetricVisualState_DistinguishesCompletePartialMissingExperimentalAndNotApplicable()
    {
        var standardDef = Def();
        var expDef = Def(acquisition: "unit.exp");

        Assert.Equal(MetricVisualKind.Complete,
            MetricVisualState.Classify(new NumericRow { Definition = standardDef, Value = new NumericValue { Number = 5, Coverage = MetricCoverage.Complete, Evidence = MetricEvidence.Verified }, Targets = 2, Valid = 2 }));
        Assert.Equal(MetricVisualKind.Partial,
            MetricVisualState.Classify(new NumericRow { Definition = standardDef, Value = new NumericValue { Number = 5, Coverage = MetricCoverage.Partial, Evidence = MetricEvidence.Verified }, Targets = 2, Valid = 1 }));
        Assert.Equal(MetricVisualKind.Partial,
            MetricVisualState.Classify(new NumericRow { Definition = standardDef, Value = new NumericValue { Number = 0, Coverage = MetricCoverage.Complete, Evidence = MetricEvidence.Verified }, Targets = 2, Valid = 1 }));
        Assert.Equal(MetricVisualKind.Missing,
            MetricVisualState.Classify(new NumericRow { Definition = standardDef, Value = new NumericValue { Number = null, Coverage = MetricCoverage.Unknown }, Targets = 2, Valid = 0 }));
        Assert.Equal(MetricVisualKind.NotApplicable,
            MetricVisualState.Classify(new NumericRow { Definition = standardDef, Value = new NumericValue { Number = null, Coverage = MetricCoverage.NotApplicable }, Targets = 0, Valid = 0 }));
        Assert.Equal(MetricVisualKind.Experimental,
            MetricVisualState.Classify(new NumericRow { Definition = expDef, Value = new NumericValue { Number = 3, Coverage = MetricCoverage.Complete, Evidence = MetricEvidence.Experimental }, Targets = 2, Valid = 2 }));
    }

    [Fact]
    public void MetricVisualState_ZeroValueNeverMisclassifiedAsMissing()
    {
        var def = Def();
        var zeroRow = new NumericRow
        {
            Definition = def,
            Value = new NumericValue { Number = 0, Coverage = MetricCoverage.Complete, Evidence = MetricEvidence.Verified },
            Targets = 1,
            Valid = 1,
        };
        Assert.Equal(MetricVisualKind.Complete, MetricVisualState.Classify(zeroRow));
        Assert.Equal(MetricVisualKind.Complete, MetricVisualState.Classify(def, zeroRow.Value));
    }

    [Fact]
    public void MetricVisualState_StatusPrecedenceKeepsMissingAndPartialDistinctFromExperimental()
    {
        var expDef = Def(acquisition: "unit.exp");
        Assert.Equal(MetricVisualKind.Missing,
            MetricVisualState.Classify(new MetricStatus(expDef, MetricAvailability.NoMeasuredColonies, false, MetricVerification.Experimental, UnitRecordingState.NotSelected, 0, 2, ["c1", "c2"])));
        Assert.Equal(MetricVisualKind.Partial,
            MetricVisualState.Classify(new MetricStatus(expDef, MetricAvailability.SomeMeasuredColonies, false, MetricVerification.Experimental, UnitRecordingState.Recording, 1, 2, ["c2"])));
        Assert.Equal(MetricVisualKind.Experimental,
            MetricVisualState.Classify(new MetricStatus(expDef, MetricAvailability.AllMeasuredColonies, false, MetricVerification.Experimental, UnitRecordingState.Recording, 2, 2, [])));
        Assert.Equal(MetricVisualKind.NotApplicable,
            MetricVisualState.Classify(new MetricStatus(expDef, MetricAvailability.NoApplicableColonies, false, MetricVerification.Experimental, UnitRecordingState.NotRequired, 0, 0, [])));
    }

    [Fact]
    public void GroupVisualStateReflectsMissingCompleteAndMixedCoverage()
    {
        var def = Def();
        MetricStatus Status(MetricAvailability availability, int measured, int targets)
            => new(def, availability, false, MetricVerification.Standard, UnitRecordingState.Recording,
                measured, targets, []);
        var missing = Status(MetricAvailability.NoMeasuredColonies, 0, 2);
        var complete = Status(MetricAvailability.AllMeasuredColonies, 2, 2);
        Assert.Equal(MetricVisualKind.Missing, MetricVisualState.ClassifyGroup([missing]));
        Assert.Equal(MetricVisualKind.Complete, MetricVisualState.ClassifyGroup([complete]));
        Assert.Equal(MetricVisualKind.Partial, MetricVisualState.ClassifyGroup([missing, complete]));
        Assert.Equal(MetricVisualKind.Missing, MetricVisualState.ClassifyGroup([]));
    }

    [Fact]
    public void DetailsTableLayout_RightAlignsNumericAndCountColumnsOnly()
    {
        Assert.Equal(CellAlignment.Left, DetailsTableLayout.ColumnAlignment(0));
        Assert.Equal(CellAlignment.Right, DetailsTableLayout.ColumnAlignment(1));
        Assert.Equal(CellAlignment.Left, DetailsTableLayout.ColumnAlignment(2));
        Assert.Equal(CellAlignment.Right, DetailsTableLayout.ColumnAlignment(3));
    }

    [Theory]
    [InlineData(0, true, false, 240f)]
    [InlineData(0, true, true, 240f)]
    [InlineData(1, true, true, 240f)]
    [InlineData(0, true, true, 120f)]
    public void DetailsTableLayout_ChartIconStaysStrictlyInsideChartHitboxAndNeverOverlapsStarOrToggle(
        int depth, bool isMetricRow, bool hasExpand, float nameWidth)
    {
        var rowWidth = nameWidth + 280f;
        var hit = DerivedMetricGroups.ComputeHitboxes(0f, rowWidth, depth, isMetricRow, hasExpand);
        var metrics = DetailsTableLayout.MeasureColumn(0, 0f, nameWidth, hit.Indent, isMetricRow, hasExpand);

        Assert.False(metrics.ChartIcon.IsEmpty);
        Assert.False(metrics.ChartIcon.Overlaps(hit.Star));
        Assert.False(metrics.ChartIcon.Overlaps(hit.Toggle));
        Assert.True(metrics.ChartIcon.X >= hit.Chart.X);
        Assert.True(metrics.ChartIcon.XMax <= nameWidth);
    }

    [Fact]
    public void DetailsTableLayout_OmitsChartIconWhenColumnTooNarrowOrBreakdownRow()
    {
        var narrow = DetailsTableLayout.MeasureColumn(0, 0f, 60f, DerivedMetricGroups.DepthIndentPixels, isMetricRow: true, hasExpand: true);
        Assert.True(narrow.ChartIcon.IsEmpty);

        var breakdown = DetailsTableLayout.MeasureColumn(0, 0f, 240f, DerivedMetricGroups.DepthIndentPixels, isMetricRow: false, hasExpand: false);
        Assert.True(breakdown.ChartIcon.IsEmpty);
    }

    [Fact]
    public void FilterSummary_CountsActiveFiltersIgnoringWhitespaceSearch()
    {
        Assert.Equal(0, FilterSummary.CountActiveFilters(false, false, false, null));
        Assert.Equal(0, FilterSummary.CountActiveFilters(false, false, false, "   "));
        Assert.Equal(1, FilterSummary.CountActiveFilters(true, false, false, ""));
        Assert.Equal(2, FilterSummary.CountActiveFilters(false, true, false, "raid"));
        Assert.Equal(4, FilterSummary.CountActiveFilters(true, true, true, "wealth"));
    }
}
