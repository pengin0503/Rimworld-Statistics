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
    public void DetailsTableLayout_RightAlignsTheCountColumnOnly()
    {
        Assert.Equal(CellAlignment.Left, DetailsTableLayout.ColumnAlignment(0));
        Assert.Equal(CellAlignment.Left, DetailsTableLayout.ColumnAlignment(1));
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

    // Widest state and count texts (headers included): Japanese "実験的 · 部分（欠けあり）" and "値あり / 対象" as measured
    // on an in-game screenshot, English "Experimental · partial (gaps)" and "Known / colonies" estimated with Arial
    // metrics at the same scale, and short ones for a list with no experimental row.
    [Theory]
    [InlineData(212f, 111f)]
    [InlineData(242f, 146f)]
    [InlineData(95f, 60f)]
    public void DetailsTableLayout_StateAndCountColumnsFitTheirWidestTextWithOnlyTheCellPadding(float stateText, float countText)
    {
        foreach (var available in new[] { 900f, 1245f, 1520f, 2200f })
        {
            var plan = DetailsTableLayout.PlanColumns(available, stateText, countText);
            Assert.Equal(available, plan.TotalWidth, 3);
            Assert.False(plan.NeedsHorizontalScroll(available));
            foreach (var (column, text) in new[] { (2, stateText), (3, countText) })
            {
                var content = DetailsTableLayout.MeasureColumn(column, 0f, plan.Widths[column], 0f, isMetricRow: true, hasExpand: true).ContentWidth;
                // Never cut, and no more than the slack left over: the column does not grow with the window.
                Assert.InRange(content - text, 0f, DetailsTableLayout.FitSlack + 0.01f);
                Assert.Equal(DetailsTableLayout.FittedWidth(column, text), plan.Widths[column]);
            }
        }
    }

    [Theory]
    [InlineData(212f, 111f)]
    [InlineData(242f, 146f)]
    [InlineData(95f, 60f)]
    public void DetailsTableLayout_ValueColumnTakesTheLargestShareOfTheRemainingWidth(float stateText, float countText)
    {
        // From the standard 1000-wide window up to a wide screen, the value column is the widest of the four.
        foreach (var available in new[] { 900f, 1000f, 1245f, 1320f, 1520f, 2200f })
        {
            var widths = DetailsTableLayout.PlanColumns(available, stateText, countText).Widths;
            Assert.Equal(1, Array.IndexOf(widths, widths.Max()));
            var nameExtra = widths[0] - DetailsTableLayout.NameMinWidth;
            var valueExtra = widths[1] - DetailsTableLayout.ValueMinWidth;
            Assert.Equal(DetailsTableLayout.ValueWeight / DetailsTableLayout.NameWeight, valueExtra / nameExtra, 3);
        }
        // At any width the value column is at least as wide as the name column.
        for (var available = 0f; available <= 3000f; available += 25f)
        {
            var widths = DetailsTableLayout.PlanColumns(available, stateText, countText).Widths;
            Assert.True(widths[1] >= widths[0], $"value {widths[1]} < name {widths[0]} at {available}");
        }
    }

    [Fact]
    public void DetailsTableLayout_TooNarrowTableKeepsEveryMinimumAndScrollsSideways()
    {
        var plan = DetailsTableLayout.PlanColumns(500f, 212f, 111f);
        Assert.Equal(new[]
        {
            DetailsTableLayout.NameMinWidth,
            DetailsTableLayout.ValueMinWidth,
            DetailsTableLayout.FittedWidth(2, 212f),
            DetailsTableLayout.FittedWidth(3, 111f),
        }, plan.Widths);
        Assert.True(plan.NeedsHorizontalScroll(500f));
    }

    [Theory]
    [InlineData(-10f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void DetailsTableLayout_FittedWidthTreatsAnUnusableMeasurementAsEmptyText(float textWidth)
    {
        Assert.Equal(DetailsTableLayout.FittedWidth(2, 0f), DetailsTableLayout.FittedWidth(2, textWidth));
        Assert.True(float.IsFinite(DetailsTableLayout.PlanColumns(1000f, textWidth, textWidth).TotalWidth));
    }

    [Fact]
    public void DetailsRowHeights_PlainRowsAreLowerAndTheProviderRowKeepsBothLines()
    {
        Assert.True(DetailsRowHeights.Row(hasSource: false) < DetailsRowHeights.Row(hasSource: true));
        Assert.True(DetailsRowHeights.Plain >= DetailsRowHeights.TitleLineHeight + 4f);
        Assert.True(DetailsRowHeights.TitleLineHeight <= DetailsRowHeights.WithSource);
        Assert.True(DetailsRowHeights.SourceLineTop + DetailsRowHeights.SourceLineHeight <= DetailsRowHeights.WithSource);
        Assert.True(DetailsRowHeights.Plain >= DetailsTableLayout.ChartIconSize + 4f);
    }

    [Fact]
    public void RowOffsets_EmptyAndSingleRow()
    {
        Assert.Equal(0, RowOffsets.Empty.Count);
        Assert.Equal(0f, RowOffsets.Empty.Total);
        Assert.Equal((0, -1), RowOffsets.Empty.Visible(0f, 500f));
        Assert.Equal(-1, RowOffsets.Empty.RowAt(0f));

        var one = new RowOffsets([28f]);
        Assert.Equal(28f, one.Total);
        Assert.Equal((0, 0), one.Visible(0f, 500f));
        Assert.Equal((0, 0), one.Visible(27.5f, 1f));
        Assert.Equal((0, -1), one.Visible(28f, 500f));
        Assert.Equal((0, 0), one.Visible(-10f, 11f));
        Assert.Equal((0, -1), one.Visible(-10f, 10f));
        Assert.Equal((0, -1), one.Visible(0f, 0f));
        Assert.Equal((0, -1), one.Visible(float.NaN, 100f));
        Assert.Equal((0, -1), one.Visible(0f, float.NaN));
    }

    [Fact]
    public void RowOffsets_MixedRowsStartAtTheirPrefixSumAndOnlyOverlappingRowsAreVisible()
    {
        // Plain, MOD, plain, MOD, MOD, plain.
        var offsets = new RowOffsets([28f, 36f, 28f, 36f, 36f, 28f]);
        Assert.Equal(6, offsets.Count);
        Assert.Equal(new[] { 0f, 28f, 64f, 92f, 128f, 164f }, Enumerable.Range(0, 6).Select(offsets.Top));
        Assert.Equal(new[] { 28f, 36f, 28f, 36f, 36f, 28f }, Enumerable.Range(0, 6).Select(offsets.Height));
        Assert.Equal(192f, offsets.Total);

        Assert.Equal((0, 0), offsets.Visible(0f, 28f));     // row 1 starts exactly at the bottom edge
        Assert.Equal((0, 1), offsets.Visible(0f, 28.5f));
        Assert.Equal((2, 3), offsets.Visible(64f, 64f));    // middle
        Assert.Equal((1, 3), offsets.Visible(63.9f, 28.2f));
        Assert.Equal((4, 5), offsets.Visible(150f, 1000f)); // end, the view taller than what is left
        Assert.Equal((0, 5), offsets.Visible(0f, 192f));
        Assert.Equal((0, -1), offsets.Visible(192f, 50f));

        Assert.Equal(0, offsets.RowAt(-5f));
        Assert.Equal(1, offsets.RowAt(28f));
        Assert.Equal(4, offsets.RowAt(163.9f));
        Assert.Equal(5, offsets.RowAt(500f));
    }

    [Fact]
    public void RowOffsets_AllPlainAllProviderAndRandomListsMatchAScanOfEveryRow()
    {
        var random = new Random(306);
        var lists = new List<float[]>
        {
            Enumerable.Repeat(DetailsRowHeights.Plain, 50).ToArray(),
            Enumerable.Repeat(DetailsRowHeights.WithSource, 50).ToArray(),
        };
        for (var n = 0; n < 40; n++)
            lists.Add(Enumerable.Range(0, random.Next(1, 120))
                .Select(_ => DetailsRowHeights.Row(random.Next(3) == 0)).ToArray());

        foreach (var heights in lists)
        {
            var offsets = new RowOffsets(heights);
            Assert.Equal(heights.Sum(), offsets.Total);
            for (var probe = 0; probe < 60; probe++)
            {
                var scrollY = (float)(random.NextDouble() * (offsets.Total + 80) - 40);
                var view = (float)(random.NextDouble() * 400);
                var (first, last) = offsets.Visible(scrollY, view);

                // Every row that overlaps [scrollY, scrollY + view) of the list, found row by row.
                var visible = new List<int>();
                float top = 0f;
                for (var i = 0; i < heights.Length; i++)
                {
                    if (top < scrollY + view && top + heights[i] > Math.Max(0f, scrollY) && view > 0f)
                        visible.Add(i);
                    top += heights[i];
                }
                if (visible.Count == 0)
                {
                    Assert.Equal((0, -1), (first, last));
                }
                else
                {
                    Assert.Equal(visible[0], first);
                    Assert.Equal(visible[^1], last);
                    Assert.Equal(visible.Count, last - first + 1);
                }
            }
        }
    }

    [Fact]
    public void RowOffsets_LongListDrawsOnlyTheRowsInTheView()
    {
        var heights = Enumerable.Range(0, 20_000).Select(i => DetailsRowHeights.Row(i % 7 == 0)).ToArray();
        var offsets = new RowOffsets(heights);
        foreach (var scrollY in new[] { 0f, offsets.Total / 2f, offsets.Total - 600f })
        {
            var (first, last) = offsets.Visible(scrollY, 600f);
            Assert.True(last - first + 1 <= 600f / DetailsRowHeights.Plain + 2f);
            Assert.True(offsets.Top(first) <= scrollY);
            Assert.True(offsets.Top(last) + offsets.Height(last) >= scrollY + 600f);
        }
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
