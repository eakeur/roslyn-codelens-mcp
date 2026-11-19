using System.Diagnostics;

namespace RoslynCodeLens;

/// <summary>
/// Probes whether classic (non-SDK) .NET Framework projects can be evaluated.
/// Roslyn loads those via the out-of-process <c>BuildHost-net472</c>, which
/// needs Visual Studio or Build Tools MSBuild on Windows. The MCP process itself
/// keeps <c>MSBuildLocator.RegisterDefaults()</c> on the .NET SDK.
/// </summary>
internal static class FrameworkMsBuildCapability
{
    public const string EnvOverrideName = "ROSLYN_CODELENS_FRAMEWORK_MSBUILD_AVAILABLE";

    public const string NonWindowsReason =
        "Classic .NET Framework projects require Windows and Visual Studio or Build Tools MSBuild.";

    public const string NotFoundReason =
        "Classic .NET Framework projects require Visual Studio or Build Tools MSBuild on this machine; none was found.";

    private static readonly object Gate = new();
    private static Result? s_cached;

    /// <summary>Test seam: override <see cref="OperatingSystem.IsWindows"/>.</summary>
    internal static Func<bool>? IsWindowsOverride { get; set; }

    /// <summary>Test seam: override <see cref="File.Exists"/>.</summary>
    internal static Func<string, bool>? FileExistsOverride { get; set; }

    /// <summary>Test seam: override <see cref="Environment.GetFolderPath"/>.</summary>
    internal static Func<Environment.SpecialFolder, string>? GetFolderPathOverride { get; set; }

    /// <summary>Test seam: override vswhere invocation; return first line of stdout or null.</summary>
    internal static Func<string, IReadOnlyList<string>, string?>? RunVsWhereOverride { get; set; }

    public readonly record struct Result(bool IsAvailable, string? UnavailableReason, string? MsBuildPath);

    public static Result Get()
    {
        lock (Gate)
        {
            if (s_cached is { } cached)
                return cached;

            s_cached = Probe();
            return s_cached.Value;
        }
    }

    public static bool IsAvailable => Get().IsAvailable;

    public static string UnavailableReason =>
        Get().UnavailableReason ?? NotFoundReason;

    /// <summary>Clears the process-wide cache and test seams. For unit tests only.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            s_cached = null;
            IsWindowsOverride = null;
            FileExistsOverride = null;
            GetFolderPathOverride = null;
            RunVsWhereOverride = null;
        }
    }

    /// <summary>Clears only the cached probe result so a new env override is observed.</summary>
    internal static void ClearCache()
    {
        lock (Gate)
            s_cached = null;
    }

    private static Result Probe()
    {
        var overrideRaw = Environment.GetEnvironmentVariable(EnvOverrideName);
        if (!string.IsNullOrWhiteSpace(overrideRaw))
        {
            if (overrideRaw is "1" or "true" or "True" or "TRUE")
                return new Result(true, null, null);
            if (overrideRaw is "0" or "false" or "False" or "FALSE")
                return new Result(false, NotFoundReason, null);
        }

        if (!(IsWindowsOverride?.Invoke() ?? OperatingSystem.IsWindows()))
            return new Result(false, NonWindowsReason, null);

        var fromVsWhere = TryFindViaVsWhere();
        if (fromVsWhere is not null)
            return new Result(true, null, fromVsWhere);

        var fromKnownPaths = TryFindViaKnownPaths();
        if (fromKnownPaths is not null)
            return new Result(true, null, fromKnownPaths);

        return new Result(false, NotFoundReason, null);
    }

    private static bool FileExists(string path) =>
        FileExistsOverride?.Invoke(path) ?? File.Exists(path);

    private static string GetFolderPath(Environment.SpecialFolder folder) =>
        GetFolderPathOverride?.Invoke(folder) ?? Environment.GetFolderPath(folder);

    private static string? TryFindViaVsWhere()
    {
        var programFilesX86 = GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(programFilesX86))
            return null;

        var vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!FileExists(vswhere))
            return null;

        var args = new[]
        {
            "-latest",
            "-products", "*",
            "-requires", "Microsoft.Component.MSBuild",
            "-find", @"MSBuild\**\Bin\MSBuild.exe",
        };

        string? output;
        try
        {
            output = RunVsWhereOverride is not null
                ? RunVsWhereOverride(vswhere, args)
                : RunProcessCaptureFirstLine(vswhere, args);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
            return null;

        var path = output.Trim();
        return FileExists(path) ? path : null;
    }

    private static string? TryFindViaKnownPaths()
    {
        var programFiles = GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(programFiles))
            return null;

        // Prefer newer Visual Studio years first; cover Community/Professional/Enterprise/BuildTools.
        string[] years = ["2026", "2022", "2019"];
        string[] editions = ["BuildTools", "Enterprise", "Professional", "Community"];

        foreach (var year in years)
        {
            foreach (var edition in editions)
            {
                var candidate = Path.Combine(
                    programFiles,
                    "Microsoft Visual Studio",
                    year,
                    edition,
                    "MSBuild",
                    "Current",
                    "Bin",
                    "MSBuild.exe");
                if (FileExists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? RunProcessCaptureFirstLine(string fileName, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process is null)
            return null;

        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        if (process.ExitCode != 0)
            return null;

        using var reader = new StringReader(stdout);
        return reader.ReadLine();
    }
}
