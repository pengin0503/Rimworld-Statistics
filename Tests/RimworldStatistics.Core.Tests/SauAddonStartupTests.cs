using RimworldStatistics.SmartApparelAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class SauAddonStartupTests
{
    [Theory]
    [InlineData(false, true, true, true, SauStartupStatus.TargetUnavailable, "")]
    [InlineData(true, false, true, true, SauStartupStatus.HostUnavailable, "host")]
    [InlineData(true, true, false, true, SauStartupStatus.PatchUnavailable, "host,patch")]
    [InlineData(true, true, true, false, SauStartupStatus.RegistrationDeclined, "host,patch,register")]
    [InlineData(true, true, true, true, SauStartupStatus.Active, "host,patch,register")]
    public void NeverRegistersAnUnpatchedCollector(bool target, bool host, bool patch, bool register,
        SauStartupStatus expected, string order)
    {
        var called = new List<string>();
        var result = SauAddonStartup.Activate(target,
            () => { called.Add("host"); return host; },
            () => { called.Add("patch"); return patch; },
            () => { called.Add("register"); return register; });
        Assert.Equal(expected, result);
        Assert.Equal(order, string.Join(",", called));
    }
}
