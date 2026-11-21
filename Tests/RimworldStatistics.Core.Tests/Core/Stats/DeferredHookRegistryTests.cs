namespace RimworldStatistics.Core.Tests;

public sealed class DeferredHookRegistryTests
{
    [Fact]
    public void NothingRunsUntilTheUnitIsRun()
    {
        var registry = new DeferredHookRegistry();
        var calls = 0;
        registry.Defer("mod.a.x", () => { calls++; return true; });
        Assert.Equal(0, calls);
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public void RunExecutesEveryInstallerOfThatUnitOnceInOrder()
    {
        var registry = new DeferredHookRegistry();
        var order = new List<string>();
        registry.Defer("mod.a.x", () => { order.Add("first"); return true; });
        registry.Defer("mod.a.x", () => { order.Add("second"); return true; });
        registry.Defer("mod.a.y", () => { order.Add("other"); return true; });
        Assert.True(registry.Run("mod.a.x"));
        Assert.True(registry.Run("mod.a.x"));   // a second metric of the same unit asks again
        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Equal(new[] { "mod.a.x" }, registry.Installed);
    }

    [Fact]
    public void UnitWithoutInstallersSucceeds()
        => Assert.True(new DeferredHookRegistry().Run("mod.external.sdk"));

    [Fact]
    public void FailureOrExceptionIsRememberedAndNeverRetried()
    {
        var registry = new DeferredHookRegistry();
        var calls = 0;
        registry.Defer("mod.a.fail", () => { calls++; return false; });
        registry.Defer("mod.a.fail", () => { calls++; return true; });
        registry.Defer("mod.a.throw", () => { calls++; throw new InvalidOperationException("target changed"); });
        Assert.False(registry.Run("mod.a.fail"));
        Assert.False(registry.Run("mod.a.fail"));
        Assert.False(registry.Run("mod.a.throw"));
        Assert.Equal(2, calls);   // the installer after a failure never runs
        Assert.Empty(registry.Installed);
    }

    [Fact]
    public void InstallerDeferredAfterRunIsIgnored()
    {
        var registry = new DeferredHookRegistry();
        Assert.True(registry.Run("mod.a.x"));
        var calls = 0;
        registry.Defer("mod.a.x", () => { calls++; return true; });
        Assert.True(registry.Run("mod.a.x"));
        Assert.Equal(0, calls);
    }
}
