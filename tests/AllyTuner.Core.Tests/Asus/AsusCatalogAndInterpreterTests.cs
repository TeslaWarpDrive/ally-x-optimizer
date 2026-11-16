using AllyTuner.Core.Asus;

namespace AllyTuner.Core.Tests.Asus;

public class AsusEndpointCatalogTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var ids = AsusEndpointCatalog.All.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Every_endpoint_cites_at_least_one_pinned_source()
    {
        foreach (var e in AsusEndpointCatalog.All)
        {
            Assert.NotEmpty(e.Sources);
            Assert.All(e.Sources, s => Assert.StartsWith("https://github.com/", s, StringComparison.Ordinal));
            // Pinned to a commit (40 hex chars) or an issue comment, never a moving branch.
            Assert.All(e.Sources, s => Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(s, "/blob/[0-9a-f]{40}/|/issues/\\d+#issuecomment-\\d+$"),
                $"{e.IdHex}: unpinned source {s}"));
        }
    }

    [Fact]
    public void Cross_checked_endpoints_have_two_independent_projects()
    {
        foreach (var e in AsusEndpointCatalog.All.Where(e => e.Agreement == SourceAgreement.CrossChecked))
        {
            var projects = e.Sources.Select(s => s.Split('/')[3] + "/" + s.Split('/')[4]).Distinct().Count();
            Assert.True(projects >= 2, $"{e.IdHex} is marked cross-checked but cites {projects} project(s).");
        }
    }

    [Fact]
    public void Phase_1_confirms_nothing_on_the_device()
    {
        // Endpoints move to ConfirmedByProbe only in a later PR that quotes Bradley's Probe report.
        Assert.All(AsusEndpointCatalog.All, e => Assert.Equal(DeviceConfirmation.Unconfirmed, e.Confirmation));
    }

    [Fact]
    public void Contains_the_ids_the_v1_features_need()
    {
        uint[] needed =
        [
            0x00120075, // performance mode
            0x001200A3, 0x001200A0, 0x001200C1, // SPL, sPPT, fPPT
            0x00110024, 0x00110025, // fan curves
            0x00120057, // battery charge limit
            0x000600C4, // VRAM
            0x001200D2, // cores
        ];
        Assert.All(needed, id => Assert.NotNull(AsusEndpointCatalog.ById(id)));
    }

    [Fact]
    public void Fan_curves_are_read_for_all_three_modes()
    {
        Assert.Equal(new uint[] { 0, 1, 2 }, AsusEndpointCatalog.ById(0x00110024)!.Arguments);
        Assert.Equal(new uint[] { 0, 1, 2 }, AsusEndpointCatalog.ById(0x00110025)!.Arguments);
        Assert.Equal(new uint[] { 0, 1, 2 }, AsusEndpointCatalog.ById(0x00110032)!.Arguments);
    }

    [Fact]
    public void Every_fan_curve_endpoint_reads_every_mode()
    {
        var curves = AsusEndpointCatalog.All.Where(e => e.Kind == AsusValueKind.FanCurve).ToList();
        Assert.NotEmpty(curves);
        Assert.All(curves, e => Assert.Equal(new uint[] { 0, 1, 2 }, e.Arguments));
    }

    [Fact]
    public void Only_the_VRAM_list_uses_the_extended_request()
    {
        var extended = AsusEndpointCatalog.All.Where(e => e.Shape == AsusRequestShape.Extended).Select(e => e.Id);
        Assert.Equal(new uint[] { 0x000600C4 }, extended);
    }

    [Fact]
    public void Research_doc_lists_every_catalogued_id()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AllyTuner.slnx")))
        {
            dir = dir.Parent;
        }

        var research = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "RESEARCH.md"));
        Assert.All(AsusEndpointCatalog.All, e => Assert.Contains($"`{e.IdHex}`", research, StringComparison.Ordinal));
    }

    [Fact]
    public void Gpu_temperature_is_flagged_as_conflicting() =>
        Assert.Equal(SourceAgreement.Conflicting, AsusEndpointCatalog.ById(0x00120097)!.Agreement);
}

public class AsusProbeInterpreterTests
{
    private static AsusRawRead Ok(uint id, byte[] reply, uint arg = 0) => new(id, arg, true, 0, 64, reply);

    [Fact]
    public void Public_RC73XA_readings_decode_to_the_expected_facts()
    {
        var readings = AsusProbeInterpreter.Interpret(
        [
            Ok(0x000600C4, Rc73xaPublicScan.VramOptions),
            Ok(0x001200D2, Rc73xaPublicScan.Scalar(Rc73xaPublicScan.CoresRaw)),
            Ok(0x001200D3, Rc73xaPublicScan.Scalar(Rc73xaPublicScan.CoresRaw)),
            Ok(0x0012006C, Rc73xaPublicScan.Scalar(Rc73xaPublicScan.ChargerTypeRaw)),
            Ok(0x00110024, Rc73xaPublicScan.CpuFanCurve),
            Ok(0x00110031, Rc73xaPublicScan.Scalar(0xFFFFFFFE)),
        ]);

        var facts = AsusProbeInterpreter.Summarize(readings);

        Assert.Equal(4096, facts.Vram!.CurrentMb);
        Assert.Equal((5, 3), facts.CoreLayout);
        Assert.Equal((5, 3), facts.CoreLayoutMax);
        Assert.Equal(2u, facts.ChargerType);
        Assert.Equal(5, facts.EndpointsPresent); // everything except the unsupported mid fan
        Assert.Equal(6, facts.EndpointsRead);
    }

    [Fact]
    public void Fan_curve_replies_are_not_read_as_status_words()
    {
        // 0x3E3B3630 has bit 16 set by accident; a fan curve's presence comes from the curve itself.
        var reading = AsusProbeInterpreter.Interpret([Ok(0x00110024, Rc73xaPublicScan.CpuFanCurve, arg: 1)]).Single();

        Assert.Null(reading.Status);
        Assert.Equal(AsusPresence.Present, reading.Presence);
        Assert.Equal("mode 1 (Silent)", reading.ArgumentLabel);
        Assert.StartsWith("48°C→1%", reading.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_or_unsupported_fan_curves()
    {
        var empty = AsusProbeInterpreter.Interpret([Ok(0x00110032, new byte[64])]).Single();
        var unsupported = AsusProbeInterpreter.Interpret([Ok(0x00110032, Rc73xaPublicScan.Scalar(0xFFFFFFFE))]).Single();

        Assert.Equal(AsusPresence.NotPresent, empty.Presence);
        Assert.Equal(AsusPresence.Unsupported, unsupported.Presence);
    }

    [Fact]
    public void Failed_calls_keep_the_windows_error()
    {
        var reading = AsusProbeInterpreter.Interpret([new AsusRawRead(0x00120075, 0, false, 31, 0, new byte[64])]).Single();

        Assert.Null(reading.Presence);
        Assert.Contains("Windows error 31", reading.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_outside_the_catalog_are_refused() =>
        Assert.Throws<InvalidOperationException>(() => AsusProbeInterpreter.Interpret([Ok(0x00999999, new byte[64])]));

    [Fact]
    public void Vram_description_lists_options()
    {
        var reading = AsusProbeInterpreter.Interpret([Ok(0x000600C4, Rc73xaPublicScan.VramOptions)]).Single();

        Assert.StartsWith("current 4 GB; options Auto, 512 MB, 1 GB, 2 GB", reading.Description, StringComparison.Ordinal);
        Assert.EndsWith("16 GB", reading.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_hex_trims_trailing_zeros()
    {
        var raw = Ok(0x00120075, Rc73xaPublicScan.Scalar(Rc73xaPublicScan.PerformanceModeRaw));
        Assert.Equal("07000100", raw.ResponseHex);
    }
}
