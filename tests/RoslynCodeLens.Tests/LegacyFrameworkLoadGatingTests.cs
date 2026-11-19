namespace RoslynCodeLens.Tests;

/// <summary>
/// Verifies that classic Framework projects are attempted when the Framework
/// MSBuild capability override is on, and still pre-skipped when it is off.
/// </summary>
public class LegacyFrameworkLoadGatingTests : IDisposable
{
    private static string LegacySolutionPath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "LegacySolution", "Legacy.sln"));

    public LegacyFrameworkLoadGatingTests()
    {
        FrameworkMsBuildCapability.ResetForTests();
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, null);
        Environment.SetEnvironmentVariable("ROSLYN_CODELENS_OPEN_PROJECT_TIMEOUT_SECONDS", null);
        FrameworkMsBuildCapability.ClearCache();
    }

    public void Dispose()
    {
        FrameworkMsBuildCapability.ResetForTests();
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, null);
        Environment.SetEnvironmentVariable("ROSLYN_CODELENS_OPEN_PROJECT_TIMEOUT_SECONDS", null);
        FrameworkMsBuildCapability.ClearCache();
    }

    [Fact]
    public async Task CapabilityOff_LegacySkippedAsLegacyKind()
    {
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, "0");
        FrameworkMsBuildCapability.ClearCache();

        var manager = await SolutionManager.CreateAsync(LegacySolutionPath);
        await manager.WaitForWarmupAsync();

        var loaded = manager.GetLoadedSolution();
        Assert.Contains(loaded.SkippedProjects, p => string.Equals(p.Kind, "Legacy", StringComparison.Ordinal));
        Assert.Contains(loaded.SkippedProjects, p =>
            p.Reason.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase)
            || p.Reason.Contains("Build Tools", StringComparison.OrdinalIgnoreCase));

        manager.Dispose();
    }

    [Fact]
    public async Task CapabilityOn_LegacyIsAttempted_NotPreSkippedAsLegacy()
    {
        // Force the loader to treat Framework MSBuild as available even on Linux CI.
        // The fixture csproj is deliberately broken, so the open should fail into
        // Failed/Timeout rather than Kind=Legacy pre-skip.
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, "1");
        Environment.SetEnvironmentVariable("ROSLYN_CODELENS_OPEN_PROJECT_TIMEOUT_SECONDS", "30");
        FrameworkMsBuildCapability.ClearCache();

        var manager = await SolutionManager.CreateAsync(LegacySolutionPath);
        await manager.WaitForWarmupAsync();

        var loaded = manager.GetLoadedSolution();
        // Pre-skip must not run: Kind=Legacy would mean the capability gate blocked the open.
        Assert.DoesNotContain(loaded.SkippedProjects, p => string.Equals(p.Kind, "Legacy", StringComparison.Ordinal));

        // With capability forced on, Roslyn is allowed to open the classic project.
        // The fixture may fail evaluation (Failed/Timeout) or still surface a project
        // shell depending on host MSBuild behaviour — either proves the attempt path.
        var legacyLoaded = loaded.Solution.Projects.Any(p =>
            string.Equals(p.Name, "LegacyLib", StringComparison.OrdinalIgnoreCase));
        var openFailed = loaded.SkippedProjects.Any(p =>
            string.Equals(p.Kind, "Failed", StringComparison.Ordinal)
            || string.Equals(p.Kind, "Timeout", StringComparison.Ordinal));
        Assert.True(legacyLoaded || openFailed,
            "Expected LegacyLib to be opened or recorded as Failed/Timeout, not pre-skipped.");

        manager.Dispose();
    }
}
