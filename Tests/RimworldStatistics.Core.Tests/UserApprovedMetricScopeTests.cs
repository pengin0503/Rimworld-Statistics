namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Approved 2026-11-11 user selection: this is a deletion/retention
/// contract, not a proposal to rename or reinterpret historical profile keys.
/// </summary>
public sealed class UserApprovedMetricScopeTests
{
    private static readonly string[] RetiredKeys =
    {
        "membership.unique",
        "membership.rejoin",
        "membership.recruit_time.min",
        "membership.recruit_time.max",
        "facilities.rooms.peak",
        "food.no_shortage.longest",
        "work.crops.cold_loss",
        "work.crops.other_loss",
        "work.crops.loss_rate",
        "facilities.medical_beds.peak",
        // Previously removed under an independent, explicit user decision.
        "facilities.running_generators.latest",
    };

    [Fact]
    public void RemovedMetricsNeverAppearInTheActiveCatalogOrNormalDetailSelection()
    {
        var visible = MetricRegistry.Compose(null, MetricDisplayPolicy.AllDetailsVisibleUnits);
        foreach (var key in RetiredKeys)
        {
            Assert.Null(MetricRegistry.Find(key));
            Assert.DoesNotContain(MetricRegistry.Catalog, def => def.StoredKey == key);
            Assert.DoesNotContain(visible, def => def.StoredKey == key);
        }
        Assert.Equal(416, MetricRegistry.Catalog.Count);
        Assert.Equal(17, SaveReader.Version);
    }

    [Fact]
    public void AllThirteenExplicitlyRetainedTopicGroupsStillHaveTheirExistingDefinitions()
    {
        // Preserved groups 1/2/3/4/6/7/8/9/10/11/12/14/15.
        var keys = new[]
        {
            HealthDeathFamily.DeathsByRaid,                                           // 1
            HealthDeathFamily.RangedKills, HealthDeathFamily.MeleeKills,             // 2
            RaidFamily.Wiped, RaidFamily.Retreated, RaidFamily.RepelledNoDown,         // 3
            MembershipFamily.Kidnaps, MembershipFamily.KidnapRescues, MembershipFamily.CaptiveMax, // 4
            CaravanFamily.Lost_, CaravanFamily.LongestExpedition,                     // 6
            MedicineFamily.SurgerySuccessRate, MedicineFamily.Implants,               // 7
            HealthDeathFamily.DeathCauses, HealthDeathFamily.DiseaseDeathRate,         // 8
            IdeologyFamily.RitualResults, IdeologyFamily.RitualSuccessRate,             // 9
            BiotechFamily.ChildStages,                                                 // 10
            MedicineFamily.DiseaseDurationMean, MedicineFamily.InfectionDurationMax,  // 11
            SocialFamily.Marriages, SocialFamily.Divorces, SocialFamily.Lovers,         // 12
            RosterSnapshot.Animals, RosterSnapshot.Mechs,                              // 14
            MedicineFamily.HostileRescues, MedicineFamily.AwayRescues,                // 15
            // Neighbouring sound measurements must not be accidentally deleted.
            MembershipFamily.FirstJoin, MembershipFamily.PrisonerRecruit,
            WorkFamily.Sown_, WorkFamily.HarvestUnits,
            FoodFamily.Nutrition, FoodFamily.HarvestPerGrowingCell,
            FacilitiesSnapshot.TurretsPeak, FacilitiesSnapshot.GenerationPeak,
        };
        foreach (var key in keys)
            Assert.NotNull(MetricRegistry.Find(key));

        foreach (var source in new[] { RosterSnapshot.Animals, RosterSnapshot.Mechs })
        {
            Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + source));
            Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MinPrefix + source));
        }
    }
}
