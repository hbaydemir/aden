using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aden;

internal static class Brand
{
    public static ImageSource Mark { get; } = Load("aden-icon-logo.png");
    public static ImageSource Wordmark { get; } = Load("aden-icon-text-logo.png");

    private static BitmapImage Load(string file)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/Aden.Desktop;component/Assets/{file}"));
        image.Freeze();
        return image;
    }
}
