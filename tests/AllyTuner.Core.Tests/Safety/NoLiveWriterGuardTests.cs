using System.Text.RegularExpressions;
using AllyTuner.Core.Safety;

namespace AllyTuner.Core.Tests.Safety;

/// <summary>
/// Phase 2 keeps every write path behind dry-run. Besides <see cref="BuildSafety.LiveWritesInThisBuild"/> being
/// false, the only <see cref="ISettingWriter"/> in the source must be <see cref="NoHardwareWriter"/>, and the app
/// must use it. A real writer arrives only in Phase 4, in a reviewed PR that changes these tests on purpose.
/// </summary>
public partial class NoLiveWriterGuardTests
{
    [Fact]
    public void The_only_setting_writer_in_src_is_the_no_hardware_writer()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var implementations = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => WriterImplementation().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToList();

        Assert.Equal(["NoHardwareWriter"], implementations);
    }

    [Fact]
    public void The_app_composes_the_safety_service_with_the_no_hardware_writer()
    {
        var services = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AllyTuner.App", "AppServices.cs"));
        Assert.Contains("NoHardwareWriter.Instance", services, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(services, @"new SafeChangeService\("));
    }

    [GeneratedRegex(@"class\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*[^{]*\bISettingWriter\b")]
    private static partial Regex WriterImplementation();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AllyTuner.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root (AllyTuner.slnx).");
    }
}
