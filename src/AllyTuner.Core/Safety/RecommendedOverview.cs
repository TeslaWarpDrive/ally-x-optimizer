namespace AllyTuner.Core.Safety;

/// <summary>
/// What "Apply recommended" would do, for a page summary: the plans it would run, the settings that differ from the
/// recommendation but cannot be changed here (and why), and those already at it.
/// </summary>
public sealed record RecommendedOverview(
    IReadOnlyList<ChangePlan> Plans,
    IReadOnlyList<(SettingDefinition Setting, string Reason)> Refused,
    IReadOnlyList<SettingDefinition> AlreadyThere)
{
    public static RecommendedOverview Build(SafeChangeService changes, IEnumerable<SettingDefinition> settings)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(settings);
        var plans = new List<ChangePlan>();
        var refused = new List<(SettingDefinition, string)>();
        var already = new List<SettingDefinition>();
        foreach (var setting in settings.Where(s => s.Recommended is not null))
        {
            var plan = changes.Plan(setting, setting.Recommended!.Value, ChangeOrigin.Recommended);
            if (!plan.CanProceed)
            {
                if (plan.Before != setting.Recommended)
                {
                    refused.Add((setting, plan.Guard.Note));
                }
                else
                {
                    already.Add(setting);
                }
            }
            else if (plan.IsNoChange)
            {
                already.Add(setting);
            }
            else
            {
                plans.Add(plan);
            }
        }

        return new RecommendedOverview(plans, refused, already);
    }

    /// <summary>One plain paragraph: what would change, and what differs but cannot be changed, with the reason.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Plans.Count == 0
                ? "Nothing to apply: everything that can be changed here is already at the recommended setting."
                : $"{Plans.Count} setting(s) differ from the recommendation and can be changed: {string.Join(", ", Plans.Select(p => p.Setting.Name))}. You see every step before anything happens.");
            if (Refused.Count > 0)
            {
                parts.Add($"{Refused.Count} differ but cannot be changed here: {string.Join(" ", Refused.Select(r => $"{r.Setting.Name}: {r.Reason}"))}");
            }

            return string.Join(" ", parts);
        }
    }
}
