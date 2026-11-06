namespace RimworldStatistics.Core.Tests;

public sealed class AutoImportGateTests
{
    [Fact]
    public void StartsOncePerSession()
    {
        var gate = new AutoImportGate();
        Assert.True(gate.ShouldStart(autoImportEnabled: true, running: false));
        Assert.False(gate.ShouldStart(autoImportEnabled: true, running: false));   // title menu is drawn every frame
    }

    [Fact]
    public void Disabled_OrRunning_DoesNotStart()
    {
        var gate = new AutoImportGate();
        Assert.False(gate.ShouldStart(autoImportEnabled: false, running: false));
        Assert.False(gate.ShouldStart(autoImportEnabled: true, running: true));
        Assert.True(gate.ShouldStart(autoImportEnabled: true, running: false));
    }

    [Fact]
    public void UserCancel_IsNotResumed()
    {
        // Pressing Cancel in the Import tab must stay cancelled while the title menu keeps asking.
        var gate = new AutoImportGate();
        Assert.True(gate.ShouldStart(true, false));
        Assert.False(gate.ShouldStart(true, false));
    }

    [Fact]
    public void GameStartInterruption_ResumesOnceOnTheTitleScreen()
    {
        // Spec §7.5: a scan stopped because a game started continues on the next return to the title screen.
        var gate = new AutoImportGate();
        Assert.True(gate.ShouldStart(true, false));
        gate.InterruptedByGame();
        Assert.True(gate.ShouldStart(true, false));
        Assert.False(gate.ShouldStart(true, false));
    }

    [Fact]
    public void Interruption_WithAutoImportOff_DoesNotStart()
    {
        var gate = new AutoImportGate();
        gate.InterruptedByGame();
        Assert.False(gate.ShouldStart(autoImportEnabled: false, running: false));
    }
}
