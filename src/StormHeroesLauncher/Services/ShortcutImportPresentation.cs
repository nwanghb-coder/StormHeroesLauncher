namespace StormHeroesLauncher.Services;
public enum ImportDisplayMode { Silent,Continuation,Diagnostic }
public sealed record ImportPresentation(ImportDisplayMode Mode,string Message);
public static class ShortcutImportPresentation
{
    public static ImportPresentation Decide(ShortcutImportResult result)
    {
        if(!result.Accepted)return new(ImportDisplayMode.Diagnostic,result.Message);
        if(result.ImportedUu && !result.CliPreparationSucceeded)return new(ImportDisplayMode.Diagnostic,"网易 UU 组件准备未完成。请检查 UU 安装后重新拖入快捷方式。\n可按住 Shift 双击启动器打开设置。");
        if(result.Complete)return new(ImportDisplayMode.Silent,"");
        var s=result.Settings;
        bool uu=s.UuLauncherPath.Length>0 && s.UuCliPath.Length>0;
        bool battle=s.BattleNetPath.Length>0 && s.HeroesSwitcherPath.Length>0;
        if(result.ImportedUu && !result.ImportedBattleNet && uu && s.BattleNetPath.Length==0)
            return new(ImportDisplayMode.Continuation,"请继续拖入“暴雪游戏平台”快捷方式");
        if(result.ImportedBattleNet && !result.ImportedUu && battle && !uu)
            return new(ImportDisplayMode.Continuation,"请继续拖入“网易 UU”快捷方式");
        if(s.BattleNetPath.Length>0 && s.HeroesSwitcherPath.Length==0)
            return new(ImportDisplayMode.Diagnostic,"未能找到风暴英雄。请确认游戏已安装，或按住 Shift 双击启动器打开设置。");
        return new(ImportDisplayMode.Diagnostic,"部分配置未完成。请检查快捷方式和程序安装后重试。\n可按住 Shift 双击启动器打开设置。");
    }
}
