using StormHeroesLauncher.Services;
using StormHeroesLauncher.Configuration;
public static class PortableTests
{
    public static void Run(Action<bool,string> check)
    {
        string old=Environment.CurrentDirectory;
        try
        {
            string before=PackagePaths.WindowHelper;
            Environment.CurrentDirectory=System.IO.Path.GetTempPath();
            check(PackagePaths.WindowHelper==before && before==System.IO.Path.Combine(AppContext.BaseDirectory,"app","StormHeroesLauncher.WindowHelper.exe"),"helper location follows executable base, never working directory");
            foreach(string root in new[]{@"C:\Users\Example\Desktop\StormHeroesLauncher",@"D:\Games\StormHeroesLauncher",@"E:\Tools\StormHeroesLauncher"})
                check(PackagePaths.HelperUnder(root)==root+@"\app\StormHeroesLauncher.WindowHelper.exe","portable helper resolves beneath supplied absolute extraction root");
            check(ShortcutImport.IsImport([@"C:\Desktop\UU.lnk"]) && ShortcutImport.IsImport([@"C:\Desktop\Battle.net.lnk"]) && ShortcutImport.IsImport([@"C:\Desktop\UU.lnk",@"C:\Desktop\Battle.net.lnk"]),"direct/desktop-shortcut dropped arguments remain import data");
            check(new SettingsStore().FilePath==System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StormHeroesLauncher","settings.json"),"portable settings remain in LOCALAPPDATA");
            check(AboutSafetyContent.LogsDirectory==System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StormHeroesLauncher","Logs"),"portable logs remain in LOCALAPPDATA");
        }
        finally {Environment.CurrentDirectory=old;}
    }
}
