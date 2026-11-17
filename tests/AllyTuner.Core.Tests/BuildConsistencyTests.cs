using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AllyTuner.Core.Tests;

/// <summary>Checks that build and packaging files agree with each other.</summary>
public partial class BuildConsistencyTests
{
    [Fact]
    public void Installer_minimum_windows_matches_the_app()
    {
        var root = RepoRoot();
        var csproj = XDocument.Load(Path.Combine(root, "src", "AllyTuner.App", "AllyTuner.App.csproj"));
        var appMin = csproj.Descendants("SupportedOSPlatformVersion").Single().Value.Trim();

        var iss = File.ReadAllText(Path.Combine(root, "installer", "AllyTuner.iss"));
        var match = MinVersionRegex().Match(iss);
        Assert.True(match.Success, "installer/AllyTuner.iss has no MinVersion line.");

        // Inno Setup uses major.minor.build; the csproj adds a revision.
        var installer = Version.Parse(match.Groups[1].Value);
        var app = Version.Parse(appMin);
        Assert.Equal((app.Major, app.Minor, app.Build), (installer.Major, installer.Minor, installer.Build));
    }

    [GeneratedRegex(@"^\s*MinVersion\s*=\s*([0-9.]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex MinVersionRegex();

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
