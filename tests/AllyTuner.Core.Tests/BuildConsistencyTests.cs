using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AllyTuner.Core.Tests;

/// <summary>Checks that build and packaging files agree with each other.</summary>
public partial class BuildConsistencyTests
{
    private static readonly string[] BrandFiles =
    [
        "assets/brand/03-handheld-boost-logo.png",
        "assets/brand/03-handheld-boost-art.png",
        "src/AllyTuner.App/Assets/LogoTile.png",
        "src/AllyTuner.App/Assets/LogoTitleBar.png",
        "src/AllyTuner.App/Assets/AppIcon.ico",
        "src/AllyTuner.App/Assets/Square44x44Logo.scale-200.png",
        "src/AllyTuner.App/Assets/Square150x150Logo.scale-200.png",
        "src/AllyTuner.App/Assets/Wide310x150Logo.scale-200.png",
        "src/AllyTuner.App/Assets/StoreLogo.png",
        "src/AllyTuner.App/Assets/SplashScreen.scale-200.png",
        "installer/wizard-small.png",
        "installer/wizard-large.png",
    ];

    private static readonly string[] ManifestLogos =
    [
        "Assets\\StoreLogo.png",
        "Assets\\Square44x44Logo.png",
        "Assets\\Square150x150Logo.png",
        "Assets\\Wide310x150Logo.png",
        "Assets\\SplashScreen.png",
    ];

    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
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

    [Fact]
    public void Brand_mark_replaces_the_placeholder()
    {
        var root = RepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "src", "AllyTuner.App", "AllyTuner.App.csproj"));
        var iss = File.ReadAllText(Path.Combine(root, "installer", "AllyTuner.iss"));
        var window = File.ReadAllText(Path.Combine(root, "src", "AllyTuner.App", "MainWindow.xaml.cs"));
        var home = File.ReadAllText(Path.Combine(root, "src", "AllyTuner.App", "Pages", "HomePage.xaml.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "packaging", "Package.appxmanifest"));

        Assert.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains("SetupIconFile=..\\src\\AllyTuner.App\\Assets\\AppIcon.ico", iss, StringComparison.Ordinal);
        Assert.Contains("WizardSmallImageFile=wizard-small.png", iss, StringComparison.Ordinal);
        Assert.Contains("WizardImageFile=wizard-large.png", iss, StringComparison.Ordinal);
        Assert.Contains("LogoTitleBar.png", window, StringComparison.Ordinal);
        Assert.Contains("LogoTile.png", home, StringComparison.Ordinal);
        var windowXaml = File.ReadAllText(Path.Combine(root, "src", "AllyTuner.App", "MainWindow.xaml"));
        var homeXaml = File.ReadAllText(Path.Combine(root, "src", "AllyTuner.App", "Pages", "HomePage.xaml"));
        Assert.DoesNotContain("AuroraGradientBrush", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AuroraGradientBrush", homeXaml, StringComparison.Ordinal);

        foreach (var relative in BrandFiles)
        {
            Assert.True(File.Exists(Path.Combine(root, relative)), relative);
        }

        foreach (var logo in ManifestLogos)
        {
            Assert.Contains(logo, manifest, StringComparison.Ordinal);
        }

        foreach (var size in IconSizes)
        {
            var name = $"Square44x44Logo.targetsize-{size}_altform-unplated.png";
            Assert.True(File.Exists(Path.Combine(root, "src", "AllyTuner.App", "Assets", name)), name);
            Assert.True(File.Exists(Path.Combine(root, "src", "AllyTuner.App", "Assets", $"Square44x44Logo.targetsize-{size}.png")), name);
        }

        var iconSizes = ReadIconSizes(Path.Combine(root, "src", "AllyTuner.App", "Assets", "AppIcon.ico"));
        Assert.Equal(IconSizes, iconSizes);
    }

    private static int[] ReadIconSizes(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var sizes = new int[count];
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            reader.ReadBytes(14);
            sizes[i] = width == 0 ? 256 : width;
            Assert.Equal(sizes[i], height == 0 ? 256 : height);
        }

        return sizes;
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
