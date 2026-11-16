using System.Text.RegularExpressions;
using AllyTuner.Core.Amd;
using AllyTuner.Core.Detection;
using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using AllyTuner.Core.Settings;
using AllyTuner.Core.Tweaks;

namespace AllyTuner.Core.Tests.Amd;

public sealed partial class AmdFeaturesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allytuner-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordingLog _log = new();
    private int _reads;

    public AmdFeaturesTests()
    {
        Facts = Read(antiLag: false, chill: false, boost: false, rsr: false);
        Reader = new AmdSettingReader(() => { _reads++; return Facts; }, _log);
        Service = new SafeChangeService(AmdFeatures.All, new ChangeJournal(_dir, _log), new WriteGate(() => AppSettings.Defaults), NoHardwareWriter.Instance, _log,
            () => PowerSource.AC, Reader, describer: Reader);
    }

    private AmdFacts Facts { get; set; }

    private AmdSettingReader Reader { get; }

    private SafeChangeService Service { get; }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static AmdFacts Read(bool antiLag, bool chill, bool boost, bool rsr, bool rsrSupported = true) => new()
    {
        Available = true,
        Features = new Dictionary<string, AmdFeatureState>
        {
            [AmdFeatures.AntiLagId] = new(true, antiLag),
            [AmdFeatures.ChillId] = new(true, chill),
            [AmdFeatures.BoostId] = new(true, boost),
            [AmdFeatures.SuperResolutionId] = new(rsrSupported, rsr),
        },
    };

    [Fact]
    public void Catalog_is_four_reversible_amd_toggles_citing_the_pinned_adlx_headers()
    {
        Assert.Equal([AmdFeatures.AntiLagId, AmdFeatures.SuperResolutionId, AmdFeatures.BoostId, AmdFeatures.ChillId], AmdFeatures.All.Select(s => s.Id));
        Assert.All(AmdFeatures.All, s =>
        {
            Assert.Equal(ValueReadBack.Readable, s.ReadBack);
            Assert.False(s.IncludedInReset);
            Assert.All(s.Sources, u => Assert.Matches("^https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/blob/32b5a740d42295c5dfe9026b9f52683da0f3af91/SDK/Include/", u));
        });
        Assert.Equal(1, AmdFeatures.AntiLag.Recommended);
        Assert.DoesNotContain(AmdFeatures.All, s => s.Name.Contains("HYPR", StringComparison.OrdinalIgnoreCase)); // no ADLX interface
    }

    [Fact]
    public void Nothing_reads_until_asked_and_then_states_follow_adlx()
    {
        Assert.False(Reader.HasRead);
        Assert.Equal(0, _reads);
        Assert.Contains("Read AMD settings", Reader.UnavailableReason(AmdFeatures.AntiLag), StringComparison.Ordinal);

        Facts = Read(antiLag: true, chill: false, boost: false, rsr: false, rsrSupported: false);
        Reader.Refresh();
        Assert.True(Reader.HasRead);
        Assert.Equal(1, Reader.TryRead(AmdFeatures.AntiLag));
        Assert.Equal(0, Reader.TryRead(AmdFeatures.Boost));
        Assert.Contains("not supported", Reader.UnavailableReason(AmdFeatures.SuperResolution), StringComparison.Ordinal);
        Assert.Contains(_log.Entries, e => e.Message.StartsWith("AMD settings read:", StringComparison.Ordinal));
        Assert.Null(Reader.TryRead(TunableSettings.SustainedPower));
    }

    [Fact]
    public void Unavailable_adlx_or_a_missing_feature_blocks_changes()
    {
        Facts = AmdFacts.Unavailable("AMD Software (ADLX) is not installed on this PC.");
        Reader.Refresh();
        Assert.Equal("AMD Software (ADLX) is not installed on this PC.", Service.Plan(AmdFeatures.AntiLag, 1).Guard.Note);

        Facts = new AmdFacts { Available = true };
        Reader.Refresh();
        Assert.Equal(TweakState.UnknownValueReason, Service.Plan(AmdFeatures.Boost, 1).Guard.Note);
    }

    [Fact]
    public void A_failing_read_is_logged_and_reported()
    {
        var reader = new AmdSettingReader(() => throw new InvalidOperationException("driver"), _log);
        var facts = reader.Refresh();
        Assert.False(facts.Available);
        Assert.Contains("driver", facts.Error, StringComparison.Ordinal);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warn && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Turning_on_anti_lag_is_simulated_with_the_adlx_call_and_can_be_undone()
    {
        Reader.Refresh();
        var plan = Service.Plan(AmdFeatures.AntiLag, 1);
        Assert.Equal("Radeon Anti-Lag: Off → On", plan.Confirmation.Lines[0]);
        var record = Assert.Single(await Service.ExecuteAsync(plan.Confirm()));
        Assert.Equal(ChangeOutcome.Simulated, record.Outcome);
        Assert.Contains("Would call IADLX3DAntiLag::SetEnabled(true) for the GPU through ADLX.", record.Detail, StringComparison.Ordinal);
        Assert.Equal(0, Service.PlanUndo(record).Plan!.Guard.Value);
    }

    [Fact]
    public void Features_that_cannot_run_together_must_be_turned_off_first()
    {
        Facts = Read(antiLag: false, chill: true, boost: false, rsr: false);
        Reader.Refresh();
        var antiLag = Service.Plan(AmdFeatures.AntiLag, 1);
        Assert.False(antiLag.CanProceed);
        Assert.Contains("cannot run together with Radeon Chill", antiLag.Guard.Note, StringComparison.Ordinal);
        Assert.Contains("Turn Radeon Chill off first", antiLag.Guard.Note, StringComparison.Ordinal);
        Assert.True(Service.Plan(AmdFeatures.AntiLag, 0).CanProceed); // turning off is always fine

        Facts = Read(antiLag: true, chill: false, boost: true, rsr: false);
        Reader.Refresh();
        Assert.Contains("Radeon Anti-Lag or Radeon Boost", Service.Plan(AmdFeatures.Chill, 1).Guard.Note, StringComparison.Ordinal);
        Assert.True(Service.Plan(AmdFeatures.SuperResolution, 1).CanProceed); // only a "some GPUs" caution
        Assert.Contains(AmdFeatures.SuperResolution.Warning, Service.Plan(AmdFeatures.SuperResolution, 1).Confirmation.Lines);
    }

    [Fact]
    public void Platform_adlx_code_has_no_setter_calls()
    {
        var platform = Path.Combine(RepoRoot(), "src", "AllyTuner.Platform.Windows");
        var offenders = Directory.EnumerateFiles(platform, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && Setter().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, string.Join('\n', offenders));
    }

    [GeneratedRegex(@"\b(SetEnabled|SetSharpness|SetResolution|SetMinFPS|SetMaxFPS)\b")]
    private static partial Regex Setter();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AllyTuner.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
