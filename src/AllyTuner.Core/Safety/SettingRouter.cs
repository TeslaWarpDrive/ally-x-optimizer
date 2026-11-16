namespace AllyTuner.Core.Safety;

/// <summary>
/// One <see cref="ISettingReader"/> and <see cref="IWriteDescriber"/> for the safety service, sending each setting to
/// the reader that owns it (Windows tweaks, ASUS and power controls). A setting no route owns reads as unknown.
/// </summary>
public sealed class SettingRouter : ISettingReader, IWriteDescriber
{
    private readonly IReadOnlyList<(Func<SettingDefinition, bool> Owns, ISettingReader Reader)> _routes;

    public SettingRouter(params (Func<SettingDefinition, bool> Owns, ISettingReader Reader)[] routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
    }

    public int? TryRead(SettingDefinition setting) => Route(setting)?.TryRead(setting);

    public string? UnavailableReason(SettingDefinition setting) => Route(setting)?.UnavailableReason(setting);

    public string? RefuseValue(SettingDefinition setting, int value) => Route(setting)?.RefuseValue(setting, value);

    public string? DescribeWrite(SettingDefinition setting, int value) => (Route(setting) as IWriteDescriber)?.DescribeWrite(setting, value);

    public string? CaptureRaw(SettingDefinition setting) => (Route(setting) as IWriteDescriber)?.CaptureRaw(setting);

    public string? DescribeRestore(SettingDefinition setting, int value, string raw) =>
        (Route(setting) as IWriteDescriber)?.DescribeRestore(setting, value, raw);

    private ISettingReader? Route(SettingDefinition setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return _routes.FirstOrDefault(r => r.Owns(setting)).Reader;
    }
}
