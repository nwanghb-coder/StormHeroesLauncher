using System.Windows;
using System.Windows.Media.Imaging;
using StormHeroesLauncher;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Services;
public static class IconTests
{
    public static void Run(AppLogger logger,Action<bool,string> check)
    {
        Exception? error=null;bool frames=false,windows=false;
        var thread=new Thread(()=>{
            try
            {
                var icon=LauncherIcon.Load();
                var uri=new Uri("pack://application:,,,/HOSLauncher;component/Assets/Launcher.ico");
                using var stream=Application.GetResourceStream(uri).Stream;
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                frames=decoder.Frames.Select(f=>f.PixelWidth).Order().SequenceEqual(new[]{16,24,32,48,64,256});
                var about=new AboutSafetyWindow(new FakeAboutActions());
                var import=new ShortcutImportWindow("offline icon check");
                var settings=new SettingsWindow(new LauncherSettings(),new SettingsStore(),new PathDiscovery(logger),new UuCliPreparation(logger,new WindowsCliValidation(),new UuCliArchive()),logger,null);
                windows=icon.IsFrozen && about.Icon!=null && import.Icon!=null && settings.Icon!=null;
                windows &= about.Title.Contains("HOSLauncher") && import.Title == "HOSLauncher" && settings.Title.StartsWith("HOSLauncher ");
                about.Close();import.Close();settings.Close(); // None are shown, loaded or clicked.
            }
            catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        check(error==null && frames,"embedded WPF icon decodes all six intended sizes");
        check(error==null && windows,"Settings/About/import windows use icon without showing or starting applications");
    }
}
