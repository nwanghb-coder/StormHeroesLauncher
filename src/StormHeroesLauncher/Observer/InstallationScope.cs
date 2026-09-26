using System.Diagnostics;
using System.IO;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.Observer;

public interface IObserverFiles
{
    void ValidatePath(string path);
    IEnumerable<string> ImmediateDirectories(string root);
    FileMetadata Read(string path, bool directory = false);
}

public sealed class ObserverFiles : IObserverFiles
{
    public void ValidatePath(string path)
    {
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new InvalidDataException("Network installations are outside observer scope");
        SafeCliPaths.NoReparse(path);
    }
    public IEnumerable<string> ImmediateDirectories(string root) => Directory.EnumerateDirectories(root);
    public FileMetadata Read(string path, bool directory = false)
    {
        try
        {
            ValidatePath(path);
            if (directory)
            {
                var info = new DirectoryInfo(path);
                return info.Exists ? new(path, "Directory", "Present", LastWriteUtc: info.LastWriteTimeUtc)
                    : new(path, "Directory", "Missing");
            }
            var file = new FileInfo(path);
            if (!file.Exists) return new(path, "File", "Missing");
            FileVersionInfo? version = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? FileVersionInfo.GetVersionInfo(path) : null;
            static string? Limit(string? s) => s == null ? null : s[..Math.Min(256, s.Length)];
            return new(path, "File", "Present", file.Length, file.LastWriteTimeUtc,
                Limit(version?.FileVersion), Limit(version?.ProductVersion), Limit(version?.CompanyName), Limit(version?.ProductName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(path, directory ? "Directory" : "File", "Unavailable"); }
    }
}

public sealed class InstallationScope
{
    private readonly IObserverFiles files;
    private readonly Dictionary<Family, HashSet<string>> observedExecutables = new()
    {
        [Family.UU] = new(StringComparer.OrdinalIgnoreCase), [Family.BattleNet] = new(StringComparer.OrdinalIgnoreCase)
    };
    public IReadOnlyDictionary<Family, string> Roots { get; }
    public InstallationScope(string uuLauncher, string battleNet, IObserverFiles files)
    {
        this.files = files;
        string Root(string executable)
        {
            if (!Path.IsPathFullyQualified(executable) || executable.StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidDataException("Observer requires known local installation paths");
            string root = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(executable))!);
            if (root.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Drive roots are not installation roots");
            files.ValidatePath(root);
            return root;
        }
        Roots = new Dictionary<Family, string> { [Family.UU] = Root(uuLauncher), [Family.BattleNet] = Root(battleNet) };
        if (Contains(Roots[Family.UU], Roots[Family.BattleNet]) || Contains(Roots[Family.BattleNet], Roots[Family.UU]))
            throw new InvalidDataException("Overlapping installation roots");
    }
    public static bool Contains(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public Family? Classify(string? path)
    {
        if (path == null || !Path.IsPathFullyQualified(path)) return null;
        path = Path.GetFullPath(path);
        foreach (var pair in Roots)
            if (Contains(pair.Value, path))
            {
                try { files.ValidatePath(path); return pair.Key; }
                catch (InvalidDataException) { return null; }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
        return null;
    }
    public FileMetadata? ReadProcessFile(string? path) => Classify(path) == null ? null : files.Read(path!);

    public IReadOnlyList<(Family Family, FileMetadata File)> Snapshot(IReadOnlyList<RelatedProcess> processes)
    {
        var result = new List<(Family, FileMetadata)>();
        foreach (var (family, root) in Roots)
        {
            files.ValidatePath(root);
            var directories = new List<string> { root };
            // Only immediate version children, at most 64 entries examined. No recursive search.
            string[] children = files.ImmediateDirectories(root).Take(65).ToArray();
            if (children.Length > 64) throw new InvalidDataException("Installation directory limit");
            foreach (string child in children)
            {
                if (!string.Equals(Path.GetDirectoryName(child), root, StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(child);
                bool version = family == Family.UU ? name.Length > 0 && name.All(char.IsAsciiDigit)
                    : name.StartsWith("Battle.net.", StringComparison.OrdinalIgnoreCase) &&
                      name[11..].Split('.').All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
                if (!version || Classify(child) != family) continue;
                directories.Add(child);
            }
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string directory in directories)
            {
                result.Add((family, files.Read(directory, true)));
                foreach (string name in family == Family.UU
                    ? new[] { "uu_launcher.exe", "uu.exe", "netease-uu-booster.7z" }
                    : new[] { "Battle.net.exe", "Battle.net Launcher.exe" })
                    paths.Add(Path.Combine(directory, name));
            }
            foreach (var process in processes)
                if (process.Identity.ExecutablePath is string path && Classify(path) == family)
                    observedExecutables[family].Add(path);
            if (observedExecutables[family].Count > 128) throw new InvalidDataException("Observed executable path limit");
            // Keep checking known executable paths after process exit; absence from the process list is not file removal.
            paths.UnionWith(observedExecutables[family]);
            foreach (string path in paths.Take(384)) result.Add((family, files.Read(path)));
        }
        return result;
    }
}
