using RimworldStatistics.Core.Tests.Fixtures;
using RimworldStatistics.VerifyHarness;

namespace RimworldStatistics.Core.Tests;

public sealed class VerifyIsolationPolicyTests
{
    [Fact]
    public void RejectsUnmarkedAndIncorrectlyMarkedFolders()
    {
        using var root = new TempDir();
        Assert.False(VerifyIsolationPolicy.HasValidMarker(null));
        Assert.False(VerifyIsolationPolicy.HasValidMarker(""));
        Assert.False(VerifyIsolationPolicy.HasValidMarker(root.Path));
        File.WriteAllText(root.File(VerifyIsolationPolicy.MarkerFileName), "other workspace");
        Assert.False(VerifyIsolationPolicy.HasValidMarker(root.Path));
    }

    [Fact]
    public void AcceptsOnlyExplicitMarkerFromTheIsolatedLauncher()
    {
        using var root = new TempDir();
        var marker = root.File(VerifyIsolationPolicy.MarkerFileName);
        File.WriteAllText(marker, VerifyIsolationPolicy.MarkerContents + Environment.NewLine);
        Assert.True(VerifyIsolationPolicy.HasValidMarker(root.Path));
        Assert.True(VerifyIsolationPolicy.HasValidMarker(Path.GetFullPath(root.Path)));
        File.WriteAllText(marker, new string('x', 129));
        Assert.False(VerifyIsolationPolicy.HasValidMarker(root.Path));
    }

    [Fact]
    public void RejectsMissingDirectoriesWithoutCreatingThem()
    {
        using var root = new TempDir();
        var missing = root.File("not-created");
        Assert.False(VerifyIsolationPolicy.HasValidMarker(missing));
        Assert.False(Directory.Exists(missing));
    }
}
