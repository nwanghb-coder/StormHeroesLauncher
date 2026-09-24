using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace StormHeroesLauncher.Configuration;
public enum BattleNetWindowMode { Minimized, Normal }
public sealed record LauncherSettings
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtraFields {get;init;}
    public string UuLauncherPath { get; init; } = "";
    public string UuCliPath { get; init; } = "";
    public string BattleNetPath { get; init; } = "";
    public string HeroesSwitcherPath { get; init; } = "";
    public BattleNetWindowMode BattleNetWindowMode { get; init; } = BattleNetWindowMode.Minimized;
    public string[] Validate(Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var errors = new List<string>();
        foreach (var (label, path, name) in new[] {
            ("UU 启动器", UuLauncherPath, "uu_launcher.exe"), ("UU CLI", UuCliPath, "uu-cli.exe"),
            ("Battle.net", BattleNetPath, "Battle.net.exe"), ("HeroesSwitcher", HeroesSwitcherPath, "HeroesSwitcher_x64.exe") })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !exists(path) ||
                !string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                errors.Add(label == "UU CLI" ? "未找到网易 UU 官方 CLI 组件（自动准备未完成）。" : $"{label}：请选择存在的 {name} 绝对路径。");
        if (!Enum.IsDefined(BattleNetWindowMode)) errors.Add("Battle.net 窗口模式无效。");
        return errors.ToArray();
    }
    public void RequireValid()
    {
        var errors = Validate();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors));
    }
}
public sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormHeroesLauncher", "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public LauncherSettings Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath), Json) ?? throw new InvalidDataException("设置文件为空。")
        : new();
    public void SaveImported(LauncherSettings settings)
    {
        // Import allows missing components; every populated known path must still pass normal validation.
        foreach(var (value,name) in new[]{(settings.UuLauncherPath,"uu_launcher.exe"),(settings.UuCliPath,"uu-cli.exe"),(settings.BattleNetPath,"Battle.net.exe"),(settings.HeroesSwitcherPath,"HeroesSwitcher_x64.exe")})
            if(!string.IsNullOrEmpty(value) && (!Path.IsPathFullyQualified(value) || !File.Exists(value) || !Path.GetFileName(value).Equals(name,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Invalid import settings");
        if(!Enum.IsDefined(settings.BattleNetWindowMode))throw new InvalidDataException("Invalid window mode");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp=FilePath+".import-"+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(settings,Json));File.Move(temp,FilePath,true);} finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public void Save(LauncherSettings settings)
    {
        settings.RequireValid();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, FilePath, true);
    }
}
public static class StartupRouting
{
    public static bool OpenSettings(IEnumerable<string> args, bool shiftHeld, bool valid, bool loadFailed = false) =>
        loadFailed || !valid || shiftHeld || args.Contains("--settings", StringComparer.OrdinalIgnoreCase);
}
