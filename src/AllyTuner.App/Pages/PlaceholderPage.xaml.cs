using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AllyTuner.Pages;

/// <summary>Shows what a not-yet-built section will contain.</summary>
public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not SectionInfo section)
        {
            return;
        }

        TitleText.Text = section.Title;
        SummaryText.Text = section.Summary;
        PhaseInfoBar.Title = $"Coming in {section.Phase}";
        PlannedList.Children.Clear();
        foreach (var item in section.Planned)
        {
            PlannedList.Children.Add(new TextBlock
            {
                Text = "•  " + item,
                Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyLargeTextStyle"],
            });
        }
    }
}
