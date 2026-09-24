using System.Windows.Media.Imaging;
namespace StormHeroesLauncher;
public static class LauncherIcon
{
    public static BitmapFrame Load()
    {
        var image=BitmapFrame.Create(new Uri("pack://application:,,,/StormHeroesLauncher;component/Assets/Launcher.ico",UriKind.Absolute));
        image.Freeze();return image;
    }
}
