using System.Text.Json;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Services;
public static class ShortcutImportTests
{
    public static void Run(AppLogger log,Action<bool,string> check)
    {
        const string ul=@"C:\UU\uu_launcher.exe",ue=@"C:\UU\123\uu.exe",bn=@"C:\Blizzard\App\Battle.net.exe",bl=@"C:\Blizzard\App\Battle.net Launcher.exe",hero=@"C:\Blizzard\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe",cli=@"C:\Cache\uu-cli.exe";
        var reader=new FakeShortcutReader();reader.Targets[@"C:\UU.lnk"]=ue;reader.Targets[@"C:\BN.lnk"]=bl;
        var identity=new FakeImportIdentity();foreach(var pair in new[]{(ul,ImportKind.Uu),(ue,ImportKind.Uu),(bn,ImportKind.BattleNet),(bl,ImportKind.BattleNet),(hero,ImportKind.Heroes),(cli,ImportKind.Cli)})identity.ValidPaths.Add(pair);
        int preparations=0;
        var importer=new ShortcutImport(reader,identity,(old,launcher)=>{preparations++;check(launcher==ul,"existing CLI Auto receives normalized UU launcher");return new(cli,"fake","ready");},log);
        var saved=new LauncherSettings {BattleNetPath=bn,HeroesSwitcherPath=hero,BattleNetWindowMode=BattleNetWindowMode.Normal,ExtraFields=new(){["FutureOption"]=JsonSerializer.SerializeToElement("keep")}};
        var uu=importer.Run([@"C:\UU.lnk"],saved);
        check(uu.Accepted && uu.Complete && uu.Settings.UuLauncherPath==ul && uu.Settings.UuCliPath==cli && preparations==1,"UU shortcut normalizes numeric-version target and prepares CLI");
        check(uu.Settings.BattleNetPath==bn && uu.Settings.HeroesSwitcherPath==hero && uu.Settings.BattleNetWindowMode==BattleNetWindowMode.Normal && uu.Settings.ExtraFields!["FutureOption"].GetString()=="keep","valid configuration and future fields preserved");
        var battle=importer.Run([@"C:\BN.lnk"],new());check(battle.Accepted && !battle.Complete && battle.Settings.BattleNetPath==bn && battle.Settings.HeroesSwitcherPath==hero && preparations==1,"Battle.net launcher normalized and Heroes sibling discovered without CLI invocation");
        var both=importer.Run([@"C:\UU.lnk",@"C:\BN.lnk"],new());var reversed=importer.Run([@"C:\BN.lnk",@"C:\UU.lnk"],new());check(both.Complete && reversed.Settings==both.Settings,"two shortcuts order-independent");
        check(importer.Run([@"C:\UU.lnk"],uu.Settings).Settings==uu.Settings,"re-import idempotent");
        int before=preparations;
        foreach(var invalid in new[]{new[]{@"C:\UU.lnk",@"C:\UU.lnk"},new[]{@"C:\bad.lnk"},new[]{@"C:\evil.cmd"},new[]{@"https:\bad.lnk"},new[]{@"C:\UU.lnk",@"C:\BN.lnk",@"C:\UU.lnk"}})
            check(!importer.Run(invalid,saved).Accepted,"invalid/duplicate/unsupported input rejected without configuration write");
        check(preparations==before,"invalid inputs do not invoke CLI Auto");
        reader.Targets[@"C:\evil.lnk"]=@"C:\Windows\System32\cmd.exe";
        check(!importer.Run([@"C:\evil.lnk"],saved).Accepted,"script-host executable target rejected");
        reader.Targets[@"C:\UU.lnk"]=@"C:\Fake\uu.exe";check(!importer.Run([@"C:\UU.lnk"],saved).Accepted,"filename alone cannot establish identity");reader.Targets[@"C:\UU.lnk"]=ue;
        // Fake shortcut contains hostile arguments; target-only reader contract exposes no execution mechanism.
        reader.IgnoredArguments="--exec=malicious & arbitrary-script";
        check(importer.Run([@"C:\BN.lnk"],uu.Settings).Accepted && reader.ArgumentReads==0,"shortcut arguments are never read or executed");
        identity.ValidPaths.Remove((hero,ImportKind.Heroes));var unresolved=importer.Run([@"C:\BN.lnk"],new());check(unresolved.Accepted && unresolved.Settings.HeroesSwitcherPath=="","missing Heroes remains unresolved; Battle.net import succeeds");
        const string metadataRoot=@"E:\Games\Heroes of the Storm";string metadataHero=System.IO.Path.Combine(metadataRoot,@"Support64\HeroesSwitcher_x64.exe");identity.ValidPaths.Add((metadataHero,ImportKind.Heroes));identity.Roots=[metadataRoot];check(importer.Run([@"C:\BN.lnk"],new()).Settings.HeroesSwitcherPath==metadataHero,"Heroes uninstall metadata fallback validated");
        check(ShortcutImport.IsImport([@"C:\UU.lnk"]) && ShortcutImport.IsImport(["--settings",@"C:\UU.lnk"]) && ShortcutImport.IsImport([@"C:\bad.cmd"]),"file arguments route to non-launch import before settings/normal mode");
        check(!ShortcutImport.IsImport([]) && !ShortcutImport.IsImport(["--settings"]) && StartupRouting.OpenSettings([],true,true) && StartupRouting.OpenSettings(["--settings"],false,true) && !StartupRouting.OpenSettings([],false,true),"normal/Shift/settings routing preserved");
        string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"StormHeroesImportTest-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(dir);
        string exe=System.IO.Path.Combine(dir,"Battle.net.exe");System.IO.File.WriteAllText(exe,"offline placeholder, never executed");
        var store=new SettingsStore(System.IO.Path.Combine(dir,"settings.json"));store.SaveImported(new(){BattleNetPath=exe,BattleNetWindowMode=BattleNetWindowMode.Normal,ExtraFields=saved.ExtraFields});var loaded=store.Load();
        check(loaded.UuLauncherPath=="" && loaded.BattleNetPath==exe && loaded.ExtraFields!["FutureOption"].GetString()=="keep","partial import saves atomically and round-trips future fields");
                string link=System.IO.Path.Combine(dir,"synthetic.lnk");Exception? shellError=null;string? actual=null;
        var sta=new Thread(()=>{
            object? shell=null,shortcut=null;
            try
            {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                dynamic writer=shell!;shortcut=writer.CreateShortcut(link);dynamic item=shortcut;
                item.TargetPath=exe;item.Arguments="--never-execute-test-data";item.Save();
                actual=new ShellShortcutReader().Target(link);
            }
            catch(Exception ex){shellError=ex;}
            finally {if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        });sta.SetApartmentState(ApartmentState.STA);sta.Start();sta.Join();
        check(shellError==null && actual==exe,"real synthetic .lnk resolves through IShellLinkW/IPersistFile without executing target or arguments");
        System.IO.File.Delete(link);System.IO.File.Delete(exe);System.IO.File.Delete(store.FilePath);System.IO.Directory.Delete(dir);
    }
}
sealed class FakeShortcutReader:IShortcutReader
{
    public Dictionary<string,string> Targets=new(StringComparer.OrdinalIgnoreCase);public string IgnoredArguments="";public int ArgumentReads=>0;
    public string Target(string path)=>Targets.TryGetValue(path,out var target) ? target : throw new InvalidDataException("Malformed link");
}
sealed class FakeImportIdentity:IImportIdentity
{
    public HashSet<(string,ImportKind)> ValidPaths=[];public string[] Roots=[];
    public bool Valid(string path,ImportKind kind)=>ValidPaths.Contains((path,kind));
    public IEnumerable<string> HeroesMetadataRoots()=>Roots;
}
