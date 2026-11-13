using RimworldStatistics.VehicleFramework;

namespace RimworldStatistics.Core.Tests;

public sealed class VehicleOwnershipCounterTests
{
    [Fact]
    public void EachPlayerVehicleCountsOnceAcrossMapCaravanAndFlight()
    {
        var parked = new object();
        var caravan = new object();
        var airplane = new object();
        var enemy = new object();
        var rows = new (object? Identity, string? DefKey, bool Owned)[]
        {
            (parked, "Vehicles/car", true),
            (parked, "Vehicles/car", true), // Also found in a second world holder
            (caravan, "Vehicles/car", true),
            (airplane, "AnotherMod/plane", true),
            (enemy, "Vehicles/car", false),
        };
        var result = VehicleOwnershipCounter.Count(rows);
        Assert.NotNull(result);
        Assert.Equal(3, result.Values.Sum());
        Assert.Equal(2, result["Vehicles/car"]);
        Assert.Equal(1, result["AnotherMod/plane"]);
    }

    [Fact]
    public void InconsistentDefForSamePawnFailsClosed()
    {
        var vehicle = new object();
        Assert.Null(VehicleOwnershipCounter.Count(new[]
        {
            (Identity: (object?)vehicle, DefKey: (string?)"Vehicles/car", Owned: true),
            (Identity: (object?)vehicle, DefKey: (string?)"Vehicles/bus", Owned: true),
        }));
    }

    [Fact]
    public void MissingOwnedDefinitionCannotBecomeFalseZero()
    {
        Assert.Null(VehicleOwnershipCounter.Count(new[]
        {
            (Identity: (object?)new object(), DefKey: (string?)null, Owned: true),
        }));
    }

    [Fact]
    public void NoOwnedVehiclesIsMeasuredZero()
    {
        var rows = new[] { (Identity: (object?)new object(), DefKey: (string?)"Vehicles/car", Owned: false) };
        var result = VehicleOwnershipCounter.Count(rows);
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
