using AllyTuner.Core.Logging;
using AllyTuner.Core.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AllyTuner.Safety;

/// <summary>
/// Shows the confirmation for a change and runs it only if the person accepts. The wording comes from
/// <see cref="ConfirmationText"/> in Core (unit-tested). Cancel is the default button, so pressing A
/// straight away never confirms anything.
/// </summary>
internal static class SafetyDialogs
{
    /// <summary>The dialog on screen, so the controller's B button can close it.</summary>
    public static ContentDialog? Current { get; private set; }

    public static async Task<bool> ConfirmAsync(XamlRoot root, ConfirmationText text)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(text);
        if (Current is not null)
        {
            return false; // one dialog at a time
        }

        var body = new StackPanel { Spacing = 10, MaxWidth = 560 };
        foreach (var line in text.Lines)
        {
            var isDryRun = line.StartsWith("Dry-run", StringComparison.Ordinal);
            var block = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 16 };
            if (isDryRun)
            {
                block.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                block.Foreground = (Brush)Application.Current.Resources["DryRunForegroundBrush"];
            }

            body.Children.Add(block);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = text.Title,
            Content = new ScrollViewer { Content = body },
            CloseButtonText = text.CloseButton,

            // Cancel stays the default (and accent) button, so pressing A straight away never confirms.
            DefaultButton = ContentDialogButton.Close,
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogButtonStyle"],
        };
        if (text.PrimaryButton is { } primary)
        {
            dialog.PrimaryButtonText = primary;
        }

        Current = dialog;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary && text.PrimaryButton is not null;
        }
        finally
        {
            Current = null;
        }
    }

    public static Task ShowMessageAsync(XamlRoot root, string title, string message) =>
        ConfirmAsync(root, new ConfirmationText(title, [message], null, "OK", true, false));

    /// <summary>Plans, confirms and executes one change. Returns the records, or nothing if cancelled.</summary>
    public static async Task<IReadOnlyList<ChangeRecord>> ConfirmAndRunAsync(XamlRoot root, ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!await ConfirmAsync(root, plan.Confirmation))
        {
            if (plan.CanProceed)
            {
                App.Services.Log.Info($"Change cancelled: {plan.Setting.Name} {plan.FromTo}.");
            }

            return [];
        }

        return await App.Services.Changes.ExecuteAsync(plan.Confirm());
    }

    /// <summary>Reset to Safe Defaults: one confirmation for the whole reset, then each step in order.</summary>
    public static async Task<IReadOnlyList<ChangeRecord>> ResetToSafeDefaultsAsync(XamlRoot root)
    {
        var changes = App.Services.Changes;
        var plans = changes.PlanResetToSafeDefaults();
        if (!await ConfirmAsync(root, ConfirmationText.ForReset(plans, changes.NotResetDirectly)))
        {
            App.Services.Log.Info("Reset to Safe Defaults cancelled.");
            return [];
        }

        App.Services.Log.Info("Reset to Safe Defaults confirmed.");
        return await changes.ExecuteAsync(ConfirmationText.ConfirmReset(plans));
    }
}
