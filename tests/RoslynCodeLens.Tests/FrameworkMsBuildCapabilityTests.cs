namespace RoslynCodeLens.Tests;

public class FrameworkMsBuildCapabilityTests : IDisposable
{
    public FrameworkMsBuildCapabilityTests()
    {
        FrameworkMsBuildCapability.ResetForTests();
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, null);
        FrameworkMsBuildCapability.ClearCache();
    }

    public void Dispose()
    {
        FrameworkMsBuildCapability.ResetForTests();
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, null);
        FrameworkMsBuildCapability.ClearCache();
    }

    [Fact]
    public void NonWindows_ReportsUnavailable()
    {
        FrameworkMsBuildCapability.IsWindowsOverride = () => false;

        var result = FrameworkMsBuildCapability.Get();

        Assert.False(result.IsAvailable);
        Assert.Equal(FrameworkMsBuildCapability.NonWindowsReason, result.UnavailableReason);
    }

    [Fact]
    public void EnvOverride_ForceAvailable()
    {
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, "1");
        FrameworkMsBuildCapability.ClearCache();
        FrameworkMsBuildCapability.IsWindowsOverride = () => false;

        var result = FrameworkMsBuildCapability.Get();

        Assert.True(result.IsAvailable);
        Assert.Null(result.UnavailableReason);
    }

    [Fact]
    public void EnvOverride_ForceUnavailable()
    {
        Environment.SetEnvironmentVariable(FrameworkMsBuildCapability.EnvOverrideName, "0");
        FrameworkMsBuildCapability.ClearCache();
        FrameworkMsBuildCapability.IsWindowsOverride = () => true;
        FrameworkMsBuildCapability.FileExistsOverride = _ => true;

        var result = FrameworkMsBuildCapability.Get();

        Assert.False(result.IsAvailable);
        Assert.Equal(FrameworkMsBuildCapability.NotFoundReason, result.UnavailableReason);
    }

    [Fact]
    public void Windows_VsWhereHit_ReportsAvailable()
    {
        FrameworkMsBuildCapability.IsWindowsOverride = () => true;
        FrameworkMsBuildCapability.GetFolderPathOverride = folder => folder switch
        {
            Environment.SpecialFolder.ProgramFilesX86 => @"C:\Program Files (x86)",
            Environment.SpecialFolder.ProgramFiles => @"C:\Program Files",
            _ => "",
        };
        var msbuildPath = @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe";
        FrameworkMsBuildCapability.FileExistsOverride = path =>
            path.EndsWith("vswhere.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, msbuildPath, StringComparison.OrdinalIgnoreCase);
        FrameworkMsBuildCapability.RunVsWhereOverride = (_, _) => msbuildPath;

        var result = FrameworkMsBuildCapability.Get();

        Assert.True(result.IsAvailable);
        Assert.Equal(msbuildPath, result.MsBuildPath);
    }

    [Fact]
    public void Windows_KnownPathHit_ReportsAvailable()
    {
        FrameworkMsBuildCapability.IsWindowsOverride = () => true;
        FrameworkMsBuildCapability.GetFolderPathOverride = folder => folder switch
        {
            Environment.SpecialFolder.ProgramFilesX86 => @"C:\Program Files (x86)",
            Environment.SpecialFolder.ProgramFiles => @"C:\Program Files",
            _ => "",
        };
        FrameworkMsBuildCapability.RunVsWhereOverride = (_, _) => null;

        string? hitPath = null;
        FrameworkMsBuildCapability.FileExistsOverride = path =>
        {
            if (path.EndsWith("vswhere.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            if (path.Contains("Microsoft Visual Studio", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("MSBuild.exe", StringComparison.OrdinalIgnoreCase))
            {
                hitPath = path;
                return true;
            }
            return false;
        };

        var result = FrameworkMsBuildCapability.Get();

        Assert.True(result.IsAvailable);
        Assert.Equal(hitPath, result.MsBuildPath);
    }

    [Fact]
    public void Windows_NoMsBuild_ReportsUnavailable()
    {
        FrameworkMsBuildCapability.IsWindowsOverride = () => true;
        FrameworkMsBuildCapability.GetFolderPathOverride = folder => folder switch
        {
            Environment.SpecialFolder.ProgramFilesX86 => @"C:\Program Files (x86)",
            Environment.SpecialFolder.ProgramFiles => @"C:\Program Files",
            _ => "",
        };
        FrameworkMsBuildCapability.FileExistsOverride = _ => false;
        FrameworkMsBuildCapability.RunVsWhereOverride = (_, _) => null;

        var result = FrameworkMsBuildCapability.Get();

        Assert.False(result.IsAvailable);
        Assert.Equal(FrameworkMsBuildCapability.NotFoundReason, result.UnavailableReason);
    }
}
