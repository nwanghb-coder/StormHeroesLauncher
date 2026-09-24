using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using StormHeroesLauncher.Services;
public static class CliPreparationTests
{
    public static async Task Run(AppLogger logger, Action<bool,string> assert)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Make(string relative, string text) { string p = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); return p; }
        string launcher = Make(@"install\uu_launcher.exe", "source:1.0");
        string v1 = Make(@"install\101\uu.exe", "source:1.0");
        string v2 = Make(@"install\202\uu.exe", "source:2.0");
        string oldArchive = Make(@"install\101\netease-uu-booster.7z", "fake source");
        string newArchive = Make(@"install\202\netease-uu-booster.7z", "fake source");
        string cache = Path.Combine(root, "cache");
        var valid = new FakeCliValidation(); var extract = new FakeCliArchive();
        UuCliPreparation Service(string? cacheName = null) => new(logger, valid, extract, cacheName ?? cache);
        string configured = Make(@"configured\uu-cli.exe", "trusted");
        assert(Service().Resolve(configured, launcher).Source == "Configured" && extract.Calls == 0, "valid configured CLI wins");
        string cached = Make(@"cache\bin\uu-cli.exe", "trusted");
        assert(Service().Resolve(Path.Combine(root,"gone.exe"), launcher).Path == cached, "invalid configured path recovers to cache");
        assert(Service().Resolve("", launcher).Source == "Cached", "verified cache preserved without speculative update");
        File.WriteAllText(cached, "unknown");
        string installed = Make(@"install\202\netease-uu-booster\bin\uu-cli.exe", "trusted");
        assert(Service().Resolve("", launcher).Path == installed, "numeric version directory finds extracted official CLI");
        File.Delete(installed);
        var prepared = Service().Resolve("", launcher);
        assert(prepared.Success && prepared.Source == "Prepared" && extract.LastArchive == newArchive, "highest signed executable version selects official archive without hardcoded directory");
        assert(File.ReadAllText(prepared.Path) == "trusted" && File.ReadAllText(newArchive) == "fake source", "preparation writes only cache and preserves source");
        File.Delete(cached);
        File.WriteAllText(v2, "untrusted");
        var afterUpdate = Service().Resolve(installed, launcher);
        assert(afterUpdate.Success && extract.LastArchive == oldArchive, "stale path recovers and rejects invalid version candidate");
        File.Delete(cached); File.WriteAllText(v2, "source:1.0");
        assert(!Service().Resolve("", launcher).Success, "same-version ambiguity stops instead of guessing active directory");
        File.WriteAllText(v2, "source:2.0");
        extract.Fail = true;
        assert(!Service().Resolve("", launcher).Success && !File.Exists(cached), "extraction failure produces no executable configuration");
        extract.Fail = false; extract.Payload = "unknown";
        assert(!Service().Resolve("", launcher).Success && !File.Exists(cached), "extracted unknown same-name executable rejected");
        extract.Payload = "trusted";
        string outside = Make(@"outside\999\uu.exe", "source:99.0");
        Make(@"outside\999\netease-uu-booster.7z", "fake source");
        Service().Resolve("", launcher);
        assert(!valid.Observed.Contains(outside) && extract.LastArchive == newArchive, "does not inspect directories outside known installation");
        File.Delete(cached); File.WriteAllText(launcher, "unknown");
        int before = extract.Calls;
        assert(!Service().Resolve("", launcher).Success && extract.Calls == before, "unverified UU installation cannot supply archive");
        foreach (string entry in new[] { "../escape.exe", @"..\escape.exe", @"C:\escape.exe", @"\\server\escape.exe", "bin/../escape.exe", "bin/file:stream", "bin /uu-cli.exe" })
        {
            bool rejected = false; try { SafeCliPaths.Under(cache, entry); } catch (InvalidDataException) { rejected = true; }
            assert(rejected, "reject unsafe archive output " + entry);
        }
        string actualArchive = Path.Combine(root, "synthetic.7z");
        void Archive(params (string Name, string Text)[] files)
        {
            using var output = File.Create(actualArchive);
            using var writer = new SevenZipWriter(output, new SevenZipWriterOptions(CompressionType.LZMA));
            foreach (var item in files) { using var input = new MemoryStream(Encoding.UTF8.GetBytes(item.Text)); writer.Write(item.Name, input, null); }
        }
        var realExtractor = new UuCliArchive();
        string extracted = Path.Combine(cache, "synthetic", "uu-cli.exe");
        Archive(("package/bin/uu-cli.exe", "trusted"), ("package/docs/readme.md", "not extracted"));
        realExtractor.Extract(actualArchive, cache, extracted);
        assert(File.ReadAllText(extracted) == "trusted" && !Directory.Exists(Path.Combine(cache,"package")), "real 7z reader extracts only CLI from synthetic archive");
        File.Delete(extracted);
        Archive(("package/bin/uu-cli.exe", "trusted"), ("../outside.txt", "blocked"));
        bool traversal = false; try { realExtractor.Extract(actualArchive, cache, extracted); } catch (InvalidDataException) { traversal = true; }
        assert(traversal && !File.Exists(extracted), "malicious 7z entry rejected before output");
        Archive(("package/bin/uu-cli.exe", "trusted"), ("package/bin/runtime.dll", "unknown"));
        bool extra = false; try { realExtractor.Extract(actualArchive, cache, extracted); } catch (InvalidDataException) { extra = true; }
        assert(extra && !File.Exists(extracted), "unsupported extra runtime files do not get blindly extracted");
        var runner = new CountingRunner();
        var guarded = new ValidatedCliRunner(valid, runner);
        await guarded.RunAsync(configured, ["--json","status"], TimeSpan.FromSeconds(1), CancellationToken.None);
        assert(runner.Calls == 1, "validated runner preserves argument boundary and execution delegation");
        File.WriteAllText(configured,"unknown");
        bool blocked = false; try { await guarded.RunAsync(configured, [], TimeSpan.FromSeconds(1), CancellationToken.None); } catch (InvalidDataException) { blocked = true; }
        assert(blocked && runner.Calls == 1, "execution-time verification blocks changed CLI");
        assert(!new WindowsCliValidation().Cli(configured).Valid, "native validator rejects non-PE unknown executable without executing it");
    }
}
sealed class FakeCliValidation : ICliValidation
{
    public HashSet<string> Observed { get; } = new(StringComparer.OrdinalIgnoreCase);
    public CliValidation Cli(string path) { Observed.Add(path); return new(File.Exists(path) && Path.GetFileName(path) == "uu-cli.exe" && File.ReadAllText(path) == "trusted", "FakeSignature"); }
    public CliValidation SourceExecutable(string path)
    {
        Observed.Add(path); string text = File.Exists(path) ? File.ReadAllText(path) : "";
        return text.StartsWith("source:") ? new(true, "FakeSignature", Version.Parse(text[7..])) : new(false, "FakeRejected");
    }
}
sealed class FakeCliArchive : IUuCliArchive
{
    public int Calls { get; private set; }
    public string? LastArchive { get; private set; }
    public bool Fail { get; set; }
    public string Payload { get; set; } = "trusted";
    public void Extract(string archive, string root, string destination)
    {
        Calls++; LastArchive = archive;
        if (Fail) throw new InvalidDataException("fake extraction failure");
        SafeCliPaths.Under(root, Path.GetRelativePath(root, destination));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination, Payload);
    }
}
sealed class CountingRunner : ICliProcessRunner
{
    public int Calls { get; private set; }
    public Task<CliProcessOutput> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token)
    { Calls++; return Task.FromResult(new CliProcessOutput(0,"{}","")); }
}
