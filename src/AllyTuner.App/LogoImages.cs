using Microsoft.UI.Xaml.Media.Imaging;

namespace AllyTuner;

/// <summary>Loads the brand tile from the files copied next to the exe.</summary>
internal static class LogoImages
{
    public static BitmapImage Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return new BitmapImage(new Uri(path));
    }
}
