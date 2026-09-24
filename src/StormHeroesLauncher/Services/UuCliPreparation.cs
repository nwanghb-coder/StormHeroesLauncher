using System.IO;
using SharpCompress.Archives.SevenZip;
namespace StormHeroesLauncher.Services;
public interface IUuCliArchive { void Extract(string archive, string cacheRoot, string destination); }
public sealed class UuCliArchive : IUuCliArchive
{
    public void Extract(string archivePath, string cacheRoot, string destination)
    {
        SafeCliPaths.NoReparse(archivePath);
        if (new FileInfo(archivePath).Length > 32 * 1024 * 1024) throw new InvalidDataException("CLI 来源包超过限制。");
        string output = SafeCliPaths.Under(cacheRoot, Path.GetRelativePath(cacheRoot, destination));
        using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = SevenZipArchive.OpenArchive(source);
        var entries = archive.Entries.Take(2049).ToArray();
        if (entries.Length > 2048) throw new InvalidDataException("来源包条目过多。");
        long total = 0;
        foreach (var entry in entries)
        {
            SafeCliPaths.Under(cacheRoot, entry.Key ?? ""); // Validate every name, but never extract it as a path.
            if (entry.IsEncrypted || entry.LinkTarget != null || entry.Size < 0 || entry.Size > 32 * 1024 * 1024) throw new InvalidDataException("不支持的压缩包条目。");
            total += entry.Size;
            if (total > 64 * 1024 * 1024) throw new InvalidDataException("来源包展开大小超限。");
        }
        var candidates = entries.Where(e => !e.IsDirectory && Path.GetFileName((e.Key ?? "").Replace('/', '\\')).Equals("uu-cli.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("官方来源包中没有唯一 CLI。");
        var selected = candidates[0];
        string parent = Path.GetDirectoryName(selected.Key!.Replace('/', '\\')) ?? "";
        if (entries.Any(e => !e.IsDirectory && e != selected && (Path.GetDirectoryName((e.Key ?? "").Replace('/', '\\')) ?? "") == parent))
            throw new InvalidDataException("此版本 CLI 含额外运行文件，尚未支持自动准备。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); SafeCliPaths.NoReparse(output);
        using var input = selected.OpenEntryStream();
        using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[65536]; long written = 0; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            written += count;
            if (written > 32 * 1024 * 1024 || written > selected.Size) throw new InvalidDataException("CLI 解压大小超限。");
            target.Write(buffer, 0, count);
        }
        if (written != selected.Size) throw new InvalidDataException("CLI 解压不完整。");
    }
}
public sealed record CliPreparationResult(string Path, string Source, string Message)
{
    public bool Success => !string.IsNullOrEmpty(Path);
}
public sealed class UuCliPreparation(AppLogger logger, ICliValidation validation, IUuCliArchive extractor, string? cacheRoot = null)
{
    public string CacheRoot { get; } = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormHeroesLauncher", "Tools", "UU");
    public CliPreparationResult Resolve(string configured, string launcher)
    {
        try { return ResolveCore(configured, launcher); }
        catch (Exception ex) { logger.Write($"CLI preparation: Extraction=Failed Error={ex.GetType().Name}"); return new("", "None", "未找到网易 UU 官方 CLI 组件，自动准备失败。请确认 UU 安装完整；详情见日志。"); }
    }
    private CliPreparationResult ResolveCore(string configured, string launcher)
    {
        bool Good(string path, string source)
        {
            var result = validation.Cli(path);
            logger.Write($"UU CLI discovery: Source={source} Path={path} Validation={(result.Valid ? "Passed" : "Failed")} Signature={result.Reason}");
            return result.Valid;
        }
        if (!string.IsNullOrWhiteSpace(configured) && Good(configured, "Configured"))
        { logger.Write("UU CLI discovery: ConfiguredPath=Valid; 保留可用组件，不根据目录编号推断升级。"); return new(configured, "Configured", "UU CLI：已自动检测"); }
        logger.Write($"UU CLI discovery: ConfiguredPath={(string.IsNullOrWhiteSpace(configured) ? "Missing" : "Invalid")}");
        foreach (string relative in new[] { @"bin\uu-cli.exe", "uu-cli.exe" })
        {
            string cached = SafeCliPaths.Under(CacheRoot, relative);
            if (File.Exists(cached) && Good(cached, "Cached"))
            { logger.Write("CachedCli=Found; 保留已验证缓存，未可靠确认更新时不替换。"); return new(cached, "Cached", "UU CLI：已自动检测"); }
        }
        logger.Write("UU CLI discovery: CachedCli=NotFoundOrInvalid");
        if (!Path.IsPathFullyQualified(launcher) || !Path.GetFileName(launcher).Equals("uu_launcher.exe", StringComparison.OrdinalIgnoreCase) || !validation.SourceExecutable(launcher).Valid)
            return new("", "None", "未找到网易 UU 官方 CLI 组件：请先确认网易 UU 安装位置和数字签名。");
        string root = Path.GetDirectoryName(launcher)!; SafeCliPaths.NoReparse(root);
        logger.Write($"UU CLI discovery: UUInstallRoot={root}");
        // Numeric immediate children only; never recursively enumerate a drive or follow junctions.
        var directories = Directory.EnumerateDirectories(root).Take(65).ToArray();
        if (directories.Length > 64) throw new InvalidDataException("UU 根目录候选过多。");
        var versions = new List<(string Directory, Version Version)>();
        foreach (string directory in directories)
        {
            if (!Path.GetFileName(directory).All(char.IsAsciiDigit)) continue;
            try { SafeCliPaths.NoReparse(directory); } catch (InvalidDataException) { continue; }
            string exe = SafeCliPaths.Under(root, Path.GetRelativePath(root, Path.Combine(directory, "uu.exe")));
            var check = validation.SourceExecutable(exe);
            if (check.Valid) versions.Add((directory, check.Version ?? new Version(0, 0)));
        }
        foreach (string folder in new[] { root }.Concat(versions.OrderByDescending(v => v.Version).Select(v => v.Directory)))
        foreach (string relative in new[] { "uu-cli.exe", @"bin\uu-cli.exe", @"netease-uu-booster\bin\uu-cli.exe" })
        {
            string candidate = SafeCliPaths.Under(root, Path.GetRelativePath(root, Path.Combine(folder, relative)));
            if (File.Exists(candidate) && Good(candidate, "UUInstallation")) return new(candidate, "UUInstallation", "UU CLI：已自动检测");
        }
        var archives = versions.Where(v => File.Exists(Path.Combine(v.Directory, "netease-uu-booster.7z"))).OrderByDescending(v => v.Version).ToArray();
        if (archives.Length == 0) { logger.Write("OfficialArchive=NotFound"); return new("", "None", "未找到网易 UU 官方 CLI 组件：当前 UU 安装中未发现配套组件包，请检查官方 UU 安装是否完整。"); }
        if (archives.Length > 1 && archives[0].Version == archives[1].Version)
            return new("", "None", "发现多个同版本 UU 组件来源，无法可靠确定当前版本，已停止自动准备。");
        string archive = SafeCliPaths.Under(root, Path.GetRelativePath(root, Path.Combine(archives[0].Directory, "netease-uu-booster.7z")));
        logger.Write($"UU CLI discovery: VersionDirectory={archives[0].Directory} OfficialArchive=Found Source=UUInstallation");
        string staging = SafeCliPaths.Under(CacheRoot, Path.Combine("staging-" + Guid.NewGuid().ToString("N"), "uu-cli.exe"));
        string final = SafeCliPaths.Under(CacheRoot, @"bin\uu-cli.exe");
        try
        {
            extractor.Extract(archive, CacheRoot, staging);
            if (!Good(staging, "Extracted")) return new("", "None", "网易 UU CLI 组件签名或产品验证失败，已拒绝使用。");
            SafeCliPaths.NoReparse(final); Directory.CreateDirectory(Path.GetDirectoryName(final)!); SafeCliPaths.NoReparse(final);
            File.Move(staging, final, true);
            if (!Good(final, "Prepared")) return new("", "None", "网易 UU CLI 缓存验证失败，已拒绝使用。");
            logger.Write($"CLI preparation: Archive={archive} Destination={final} Extraction=Success Signature=Valid");
            return new(final, "Prepared", "UU CLI：已自动准备");
        }
        finally
        {
            // Only remove our single staging file; no recursive removal, never touch installation files.
            SafeCliPaths.NoReparse(staging);
            if (File.Exists(staging)) File.Delete(staging);
        }
    }
}
