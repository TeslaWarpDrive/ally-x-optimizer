using System.Text.RegularExpressions;

namespace AllyTuner.Core.Tests;

/// <summary>
/// Phase 1 is read-only. These tests scan the source tree (src/, all projects) so a write path cannot
/// slip in unnoticed. When Phase 4 adds writes behind the safety framework, these rules move with it.
/// </summary>
public partial class ReadOnlyGuardTests
{
    private static readonly string[] ForbiddenPatterns =
    [
        @"0x53564544",                         // ASUS DEVS (set device) method ID
        @"0x54494E49",                         // ASUS INIT method ID
        @"0x474F4457",                         // ASUS WDOG method ID
        @"""SetGamingFullScreenExperience""",   // would switch FSE on/off
        @"\bPowerWrite[A-Za-z]*\s*\(",         // power-plan writes
        @"\bPowerSetActiveScheme\s*\(",
        @"\bRegSetValue[A-Za-z]*\s*\(",        // registry writes
        @"\.SetValue\s*\(",                    // Microsoft.Win32.RegistryKey.SetValue
        @"InvokeMethod\s*\(",                  // WMI method calls (DEVS via AsusAtkWmi_WMNB)
        @"PowerSetUserConfigured[A-Za-z]*PowerMode\s*\(", // power-mode writes (Phase 3 only reads them)
        @"\.DeleteValue\s*\(",                // registry deletes
        @"\.CreateSubKey\s*\(",               // registry key creation (opens for write)
        @"\.OpenSubKey\s*\([^)]*,\s*(writable:\s*)?(true|RegistryKeyPermissionCheck\.ReadWriteSubTree)", // writable registry opens
        @"ProcessStartInfo\s*\([^)]*dism",       // DISM feature changes
    ];

    [Fact]
    public void Phase_1_source_contains_no_write_paths()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComment(lines[i]);
                foreach (var pattern in ForbiddenPatterns)
                {
                    if (Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase))
                    {
                        offenders.Add($"{Path.GetRelativePath(src, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Write paths found in Phase 1 source:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Guard_actually_scans_the_platform_code()
    {
        var platform = Path.Combine(FindRepoRoot(), "src", "AllyTuner.Platform.Windows");
        Assert.NotEmpty(Directory.EnumerateFiles(platform, "*.cs", SearchOption.AllDirectories));
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        var code = index >= 0 ? line[..index] : line;
        var trimmed = code.TrimStart();
        return trimmed.StartsWith('*') || trimmed.StartsWith("///", StringComparison.Ordinal) ? string.Empty : code;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AllyTuner.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root (AllyTuner.slnx).");
    }
}
