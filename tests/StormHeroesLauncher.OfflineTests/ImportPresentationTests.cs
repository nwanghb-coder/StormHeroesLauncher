using StormHeroesLauncher.Services;
using StormHeroesLauncher.Configuration;
public static class ImportPresentationTests
{
    public static void Run(Action<bool,string> check)
    {
        var uu=new LauncherSettings {UuLauncherPath="valid",UuCliPath="valid"};
        var battle=new LauncherSettings {BattleNetPath="valid",HeroesSwitcherPath="valid"};
        var full=uu with {BattleNetPath="valid",HeroesSwitcherPath="valid"};
        ShortcutImportResult Result(LauncherSettings s,bool u,bool b,bool complete=false)=>new(s,true,complete,"internal checklist") {ImportedUu=u,ImportedBattleNet=b};
        var p=ShortcutImportPresentation.Decide(Result(uu,true,false));
        check(p.Mode==ImportDisplayMode.Continuation && p.Message=="请继续拖入“暴雪游戏平台”快捷方式","UU-only missing counterpart shows exact minimal Battle.net prompt");
        p=ShortcutImportPresentation.Decide(Result(battle,false,true));
        check(p.Mode==ImportDisplayMode.Continuation && p.Message=="请继续拖入“网易 UU”快捷方式","Battle.net-only missing counterpart shows exact minimal UU prompt");
        foreach(var pair in new[]{(true,false),(false,true),(true,true)})
        {p=ShortcutImportPresentation.Decide(Result(full,pair.Item1,pair.Item2,true));check(p.Mode==ImportDisplayMode.Silent && p.Message=="","complete single or dual import exits silently");}
        p=ShortcutImportPresentation.Decide(Result(battle with {HeroesSwitcherPath=""},false,true));
        check(p.Mode==ImportDisplayMode.Diagnostic && p.Message.Contains("风暴英雄"),"Heroes unresolved remains diagnostic");
        p=ShortcutImportPresentation.Decide(new(new(),false,false,"无法识别此快捷方式。"));
        check(p.Mode==ImportDisplayMode.Diagnostic && p.Message=="无法识别此快捷方式。","invalid shortcut remains user-facing error");
        p=ShortcutImportPresentation.Decide(Result(full,true,false,true) with {CliPreparationSucceeded=false});
        check(p.Mode==ImportDisplayMode.Diagnostic,"CLI preparation failure overrides silent success");
        p=ShortcutImportPresentation.Decide(Result(full,true,true,true) with {Accepted=false,Message="无法保存配置。"});
        check(p.Mode==ImportDisplayMode.Diagnostic,"save failure never exits silently");
    }
}
