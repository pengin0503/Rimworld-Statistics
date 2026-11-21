namespace RimworldStatistics.Core.Tests;

public sealed class NumericSnapshotRosterTests
{
    [Fact]
    public void CountsLivingPlayerPawnsOnlyOnceAcrossCaravanAndMapIndexes()
    {
        var human = new RosterPawnFact
        {
            Id = "Thing_A", Kind = RosterKind.FreeColonist, AgeYears = 28,
            Augmented = true, Injured = true,
            Skills = new[]
            {
                new RosterSkillFact("Shooting", 20, "Major"), new RosterSkillFact("Crafting", 20, "Minor"),
            },
        };
        var result = RosterSnapshot.Build(new[]
        {
            human, human,
            new RosterPawnFact { Id = "Thing_B", Kind = RosterKind.FreeColonist, AgeYears = 42,
                Skills = new[]{ new RosterSkillFact("Shooting", 10, "None") } },
            new RosterPawnFact { Id = "Thing_P", Kind = RosterKind.Prisoner },
            new RosterPawnFact { Id = "Thing_Cow", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow" },
            new RosterPawnFact { Id = "Thing_Alpaca", Kind = RosterKind.ColonyAnimal, RaceDef = "Alpaca" },
            new RosterPawnFact { Id = "Thing_Mech", Kind = RosterKind.ColonyMech },
            new RosterPawnFact { Id = "Thing_Enemy", Kind = RosterKind.Other },
        });
        long N(string key) => result[key].Values[""].IntegerValue!.Value;
        Assert.Equal(2, N(RosterSnapshot.Free));
        Assert.Equal(1, N(RosterSnapshot.Prisoners));
        Assert.Equal(2, N(RosterSnapshot.Animals));
        Assert.Equal(1, N(RosterSnapshot.Mechs));
        Assert.Equal(1, N(RosterSnapshot.AugmentedPeak));
        Assert.Equal(1, N(RosterSnapshot.Skill20PawnsPeak));
        Assert.Equal(2, N(RosterSnapshot.Skill20PerPawnPeak));
        Assert.Equal(2, N(RosterSnapshot.AnimalDefsPeak));
        Assert.Equal(1, N(RosterSnapshot.InjuredPeak));
        Assert.Equal(42d, result[RosterSnapshot.Oldest].Values[""].RealValue);
        var mean = result[RosterSnapshot.AgeMean].Values[""];
        Assert.Equal(35d, mean.RealValue);
        Assert.Equal(70d, mean.Numerator);
        Assert.Equal(2d, mean.Denominator);
        Assert.Equal(1, result[RosterSnapshot.Passions].Values["Shooting:Major"].IntegerValue);
        Assert.Equal(1, result[RosterSnapshot.Passions].Values["Shooting:None"].IntegerValue);
    }

    [Fact]
    public void EmptyRosterProducesKnownZeroCountsButNoAge()
    {
        var result = RosterSnapshot.Build(Array.Empty<RosterPawnFact>());
        Assert.Equal(0, result[RosterSnapshot.Free].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, result[RosterSnapshot.AgeMean].Values[""].Coverage);
        Assert.Equal(MetricCoverage.NotApplicable, result[RosterSnapshot.Oldest].Values[""].Coverage);
    }

    [Fact]
    public void SlaveStatusHasIndependentLiveSnapshotCountAndCannotBeDoubleCounted()
    {
        Assert.True(Enum.TryParse<RosterKind>("Slave", out var slave));
        var r = RosterSnapshot.Build(new[] {
            new RosterPawnFact { Id = "S1", Kind = slave },
            new RosterPawnFact { Id = "S1", Kind = slave },
            new RosterPawnFact { Id = "F1", Kind = RosterKind.FreeColonist, AgeYears = 20 },
            new RosterPawnFact { Id = "P1", Kind = RosterKind.Prisoner },
        });
        Assert.Equal(1, r["population.snapshot.slaves"].Values[""].IntegerValue);
        Assert.Equal(1, r[RosterSnapshot.Free].Values[""].IntegerValue);
        Assert.Equal(1, r[RosterSnapshot.Prisoners].Values[""].IntegerValue);
        Assert.NotNull(MetricRegistry.Find("population.snapshot.slaves"));
        Assert.Null(MetricRegistry.Find("facilities.running_generators.latest"));
    }

    [Fact]
    public void SpeciesCountsAreExclusiveAcrossStatusesAndMissingOwnershipIsPartial()
    {
        var colonist = new RosterPawnFact { Id = "A", Kind = RosterKind.FreeColonist,
            Species = "race.one/Fox", AgeYears = 24 };
        var snapshot = RosterSnapshot.Build([
            colonist, colonist,
            new RosterPawnFact { Id = "B", Kind = RosterKind.Prisoner, Species = "race.two/Fox" },
            new RosterPawnFact { Id = "C", Kind = RosterKind.Slave },
        ]);
        var free = snapshot[RosterSnapshot.FreeSpeciesPeak].Values;
        var prisoner = snapshot[RosterSnapshot.PrisonerSpeciesPeak].Values;
        var slave = snapshot[RosterSnapshot.SlaveSpeciesPeak].Values;
        Assert.Equal(1, free["race.one/Fox"].IntegerValue);
        Assert.DoesNotContain("race.two/Fox", free.Keys);
        Assert.Equal(1, prisoner["race.two/Fox"].IntegerValue);
        Assert.Equal(1, slave[MetricKeys.UnknownBucket].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, slave[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void AnimalNutritionNeed_AddsEachColonyAnimalOnce_ByRace()
    {
        // The game's own rate per animal: an adult cow, a calf (a juvenile life stage eats less), a MOD animal, an animal
        // that eats nothing now (no food need, or in a cryptosleep casket), listed twice by a map and a caravan index.
        var cow = new RosterPawnFact { Id = "Thing_Cow1", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow", Species = "ludeon.rimworld/Cow", NutritionPerDay = 2.6 };
        var result = RosterSnapshot.Build([
            cow, cow,
            new RosterPawnFact { Id = "Thing_Cow2", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow", Species = "ludeon.rimworld/Cow", NutritionPerDay = 0.65 },
            new RosterPawnFact { Id = "Thing_Moose1", Kind = RosterKind.ColonyAnimal, RaceDef = "Moose", Species = "some.mod/Moose", NutritionPerDay = 3.2 },
            new RosterPawnFact { Id = "Thing_Asleep1", Kind = RosterKind.ColonyAnimal, RaceDef = "Husky", Species = "ludeon.rimworld/Husky", NutritionPerDay = 0 },
            // Only colony animals: a colonist, a mech or another faction's animal adds nothing.
            new RosterPawnFact { Id = "Thing_Human1", Kind = RosterKind.FreeColonist, AgeYears = 30, NutritionPerDay = 1.6 },
            new RosterPawnFact { Id = "Thing_Mech1", Kind = RosterKind.ColonyMech },
            new RosterPawnFact { Id = "Thing_Wild1", Kind = RosterKind.Other, NutritionPerDay = 9 },
        ]);
        var need = result[RosterSnapshot.AnimalNutritionPerDay].Values;
        Assert.Equal(MetricCoverage.SnapshotOnly, need[MetricEntry.Scalar].Coverage);
        Assert.Equal(6.45, need[MetricEntry.Scalar].RealValue!.Value, 9);
        Assert.Equal(3.25, need["ludeon.rimworld/Cow"].RealValue!.Value, 9);
        Assert.Equal(3.2, need["some.mod/Moose"].RealValue);
        Assert.Equal(0d, need["ludeon.rimworld/Husky"].RealValue);
        Assert.Equal(4, need.Count);
        Assert.Equal(4, result[RosterSnapshot.Animals].Values[MetricEntry.Scalar].IntegerValue);   // the count is unchanged
    }

    [Fact]
    public void AnimalNutritionNeed_IsATrueZeroWithoutAnimals_AndPartialWhenAnAnimalCannotBeRead()
    {
        var none = RosterSnapshot.Build([new RosterPawnFact { Id = "Thing_Human1", Kind = RosterKind.FreeColonist, AgeYears = 30 }])
            [RosterSnapshot.AnimalNutritionPerDay].Values;
        Assert.Equal((MetricCoverage.SnapshotOnly, (double?)0d, 1), (none[MetricEntry.Scalar].Coverage, none[MetricEntry.Scalar].RealValue, none.Count));

        static MetricValue Need(params RosterPawnFact[] facts) => RosterSnapshot.Build(facts)[RosterSnapshot.AnimalNutritionPerDay].Values[MetricEntry.Scalar];
        var known = new RosterPawnFact { Id = "Thing_Cow1", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow", Species = "ludeon.rimworld/Cow", NutritionPerDay = 2 };
        foreach (var unread in new double?[] { null, double.NaN, double.PositiveInfinity, -1 })
        {
            // The total is a lower bound of what the readable animals need, never a false complete value.
            var value = Need(known, new RosterPawnFact { Id = "Thing_X", Kind = RosterKind.ColonyAnimal, RaceDef = "X", Species = "x/X", NutritionPerDay = unread });
            Assert.Equal((MetricCoverage.Partial, (double?)2d), (value.Coverage, value.RealValue));
        }
        var noRace = RosterSnapshot.Build([known, new RosterPawnFact { Id = "Thing_Y", Kind = RosterKind.ColonyAnimal, NutritionPerDay = 1 }])
            [RosterSnapshot.AnimalNutritionPerDay].Values;
        Assert.Equal((MetricCoverage.Partial, (double?)3d, (double?)1d), (noRace[MetricEntry.Scalar].Coverage, noRace[MetricEntry.Scalar].RealValue, noRace[MetricKeys.UnknownBucket].RealValue));
        // A pawn that could not be read at all may have been an animal.
        Assert.Equal(MetricCoverage.Partial, RosterSnapshot.Build([known], partial: true)[RosterSnapshot.AnimalNutritionPerDay].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void AnimalNutritionNeed_IsTheLatestSavedState_NeverASumOfSaves()
    {
        var definition = MetricRegistry.Find(RosterSnapshot.AnimalNutritionPerDay)!;
        Assert.Equal((MetricUnit.Nutrition, MetricCategory.Animals, MetricObservationSource.SaveSnapshot, "roster", true, OverallAggregation.Sum, ColonySelection.Representative),
            (definition.Unit, definition.Category, definition.Source, definition.Acquisition, definition.HasBreakdown, definition.Overall, definition.Selection));
        Assert.False(HistoryChanges.IsRunningTotal(definition));
        Assert.Null(MetricRates.PerDay(definition, new NumericValue { Number = 5, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = 600000 }));

        var id = new ColonyIdentity
        {
            SeedString = "s", PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
            StartingPawnIds = ["Thing_Human1"], ModCampaignId = "herd",
        };
        var now = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation
        {
            FileName = "f.rws", FileSize = 1, LastWriteUtc = now, ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok, Identity = id, Values = { TicksGame = 120000 },
        });
        MetricObservation Snapshot(string key, int tick, double need)
        {
            var o = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = key, Identity = id, SaveTick = tick, ObservedUtc = now.AddTicks(tick),
            };
            foreach (var (k, entry) in RosterSnapshot.Build([new RosterPawnFact
                { Id = "Thing_Cow1", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow", Species = "ludeon.rimworld/Cow", NutritionPerDay = need }]))
                o.Metrics[k] = entry;
            return o;
        }
        p.MetricObservations.Add(Snapshot("a", 60000, 3));
        p.MetricObservations.Add(Snapshot("b", 120000, 5));
        // An older record without the statistic is no reading of it, not a zero.
        p.MetricObservations.Add(new MetricObservation { Source = MetricObservationSource.SaveSnapshot, Key = "old", Identity = id, SaveTick = 30000, ObservedUtc = now });
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), MetricRegistry.Catalog);
        var value = view.Colonies.Single().Rows.Single(r => r.Definition.Key == RosterSnapshot.AnimalNutritionPerDay).Value;
        Assert.Equal((5d, MetricCoverage.SnapshotOnly), (value.Number, value.Coverage));
    }

    [Fact]
    public void CatalogRetainsPeakSelectionWithoutPublishing()
    {
        Assert.Equal(ColonySelection.ObservationMax, MetricRegistry.Find(RosterSnapshot.InjuredPeak)!.Selection);
        Assert.Equal(ColonySelection.ObservationMax, MetricRegistry.Find(RosterSnapshot.AugmentedPeak)!.Selection);
        Assert.False(MetricRegistry.Find(RosterSnapshot.Free)!.Published);
    }
}
