using System.IO;
using StormHeroesLauncher.Configuration;
namespace StormHeroesLauncher.Services;
public enum ImportKind { Uu, BattleNet, Heroes, Cli }
public interface IImportIdentity { bool Valid(string path,ImportKind kind); IEnumerable<string> HeroesMetadataRoots(); }
public sealed record ShortcutImportResult(LauncherSettings Settings,bool Accepted,bool Complete,string Message)
{ public bool ImportedUu {get;init;} public bool ImportedBattleNet {get;init;} public bool CliPreparationSucceeded {get;init;} = true; }
public sealed class ShortcutImport(IShortcutReader reader,IImportIdentity identity,Func<string,string,CliPreparationResult> prepare,AppLogger log)
{
    // Unknown arguments also enter non-launch setup mode, then are rejected safely.
    public static bool IsImport(string[] args)=>args.Any(a=>!a.Equals("--settings",StringComparison.OrdinalIgnoreCase));
    public static string LocalPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || path.Length<3 || !char.IsAsciiLetter(path[0]) || path[1]!=':' || path[2]!='\\' || path[2..].Contains(':')) throw new InvalidDataException("Not a local absolute path");
        return Path.GetFullPath(path);
    }
    public ShortcutImportResult Run(string[] inputs,LauncherSettings saved)
    {
        try
        {
            log.Write($"Shortcut import: InputCount={inputs.Length}");
            if(inputs.Length is <1 or >2) throw new InvalidDataException("Input count");
            var resolved=new Dictionary<ImportKind,string>();
            foreach(string input in inputs)
            {
                if(!Path.GetExtension(LocalPath(input)).Equals(".lnk",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Input type");
                string target=LocalPath(reader.Target(input));string name=Path.GetFileName(target);
                ImportKind kind;string normalized;
                if(name.Equals("uu.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("uu_launcher.exe",StringComparison.OrdinalIgnoreCase))
                {
                    kind=ImportKind.Uu;if(!identity.Valid(target,kind)) throw new InvalidDataException("UU identity");
                    string dir=Path.GetDirectoryName(target)!;
                    var candidates=new List<string>{Path.Combine(dir,"uu_launcher.exe")};
                    if(Path.GetFileName(dir).All(char.IsAsciiDigit)) candidates.Add(Path.Combine(Path.GetDirectoryName(dir)!,"uu_launcher.exe"));
                    normalized=candidates.FirstOrDefault(p=>identity.Valid(p,kind)) ?? throw new InvalidDataException("UU launcher missing");
                }
                else if(name.Equals("Battle.net.exe",StringComparison.OrdinalIgnoreCase)||name.Equals("Battle.net Launcher.exe",StringComparison.OrdinalIgnoreCase))
                {
                    kind=ImportKind.BattleNet;if(!identity.Valid(target,kind)) throw new InvalidDataException("Battle.net identity");
                    normalized=Path.Combine(Path.GetDirectoryName(target)!,"Battle.net.exe");
                    if(!identity.Valid(normalized,kind)) throw new InvalidDataException("Battle.net main missing");
                }
                else throw new InvalidDataException("Unsupported target");
                if(!resolved.TryAdd(kind,normalized)) throw new InvalidDataException("Duplicate component");
                log.Write($"Shortcut import: Type=Lnk Classification={kind} TargetValidated=True");
            }
            var result=saved; bool cliPreparationSucceeded=true;
            if(resolved.TryGetValue(ImportKind.Uu,out string? uu))
            {
                var cli=prepare(saved.UuCliPath,uu); cliPreparationSucceeded=cli.Success;
                result=result with {UuLauncherPath=uu,UuCliPath=cli.Success && identity.Valid(cli.Path,ImportKind.Cli) ? cli.Path : identity.Valid(saved.UuCliPath,ImportKind.Cli) ? saved.UuCliPath : ""};
                log.Write($"Shortcut import: UuCli={(cli.Success ? "PreparedOrExisting" : "Failed")}");
            }
            if(resolved.TryGetValue(ImportKind.BattleNet,out string? battle))
                result=result with {BattleNetPath=battle,HeroesSwitcherPath=FindHeroes(saved.HeroesSwitcherPath,battle)};
            // Preserve valid existing components, and do not persist known broken values as resolved paths.
            result=result with {
                UuLauncherPath=identity.Valid(result.UuLauncherPath,ImportKind.Uu) ? result.UuLauncherPath : "",
                UuCliPath=identity.Valid(result.UuCliPath,ImportKind.Cli) ? result.UuCliPath : "",
                BattleNetPath=identity.Valid(result.BattleNetPath,ImportKind.BattleNet) ? result.BattleNetPath : "",
                HeroesSwitcherPath=identity.Valid(result.HeroesSwitcherPath,ImportKind.Heroes) ? result.HeroesSwitcherPath : ""};
            bool complete=new[]{result.UuLauncherPath,result.UuCliPath,result.BattleNetPath,result.HeroesSwitcherPath}.All(p=>p.Length>0);
            log.Write($"Shortcut import: UuLauncher={(result.UuLauncherPath.Length>0 ? "Valid" : "Missing")} BattleNet={(result.BattleNetPath.Length>0 ? "Valid" : "Missing")} HeroesSwitcher={(result.HeroesSwitcherPath.Length>0 ? "Valid" : "NotFound")} ImportResult={(complete ? "Complete" : "Partial")}");
            string Line(string label,string path)=>path.Length>0 ? "✓ "+label : "— "+label+" 未配置";
            return new(result,true,complete,string.Join("\n",complete ? "配置完成" : "已完成部分配置",Line("网易 UU",result.UuLauncherPath),Line("UU CLI",result.UuCliPath),Line("Battle.net",result.BattleNetPath),Line("风暴英雄",result.HeroesSwitcherPath))) { ImportedUu=resolved.ContainsKey(ImportKind.Uu), ImportedBattleNet=resolved.ContainsKey(ImportKind.BattleNet), CliPreparationSucceeded=cliPreparationSucceeded };
        }
        catch(Exception ex)
        {
            log.Write($"Shortcut import: ImportResult=Failed Exception={ex.GetType().Name}");
            return new(saved,false,false,"无法识别此快捷方式。\n仅支持网易 UU 与 Battle.net 的 Windows 快捷方式。\n请检查目标程序是否存在、来源可信，且没有重复拖入同一组件。");
        }
    }
    private string FindHeroes(string saved,string battle)
    {
        if(identity.Valid(saved,ImportKind.Heroes)) return saved;
        string dir=Path.GetDirectoryName(battle)!;
        string parent=Path.GetDirectoryName(dir) ?? dir;
        string Candidate(string root)=>Path.Combine(root,"Heroes of the Storm","Support64","HeroesSwitcher_x64.exe");
        string derived=Candidate(parent);if(identity.Valid(derived,ImportKind.Heroes)) return derived;
        foreach(string root in identity.HeroesMetadataRoots().Take(32))
        {
            try { string p=Path.Combine(LocalPath(root),"Support64","HeroesSwitcher_x64.exe");if(identity.Valid(p,ImportKind.Heroes))return p; } catch(ArgumentException){} catch(InvalidDataException){}
        }
        return new[]{Candidate(dir),Path.Combine(dir,"Support64","HeroesSwitcher_x64.exe")}.FirstOrDefault(p=>identity.Valid(p,ImportKind.Heroes)) ?? "";
    }
}
