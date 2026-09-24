using System.Text;
using System.Text.Json;
using System.IO.Pipes;
using StormHeroesLauncher.Services;
using StormHeroesLauncher.Models;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.WindowSupport;
public static class HelperResultTests
{
    public static async Task Run(AppLogger logger,Action<bool,string> check)
    {
        var report = new UuTrayReport(true,true,10,20,"UUMAINFORMV40","Posted",false,true,0);
        // Establish that the old DTO itself round-trips; no separate cold/warm shape exists.
        check(JsonSerializer.Deserialize<UuTrayReport>(JsonSerializer.SerializeToUtf8Bytes(report)) == report,"legacy shared report schema round-trips");
        bool oldThrows = false; try { JsonSerializer.Deserialize<UuTrayReport>(Array.Empty<byte>()); } catch(JsonException) { oldThrows = true; }
        check(oldThrows,"reproduces old unguarded JsonException for empty result");
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var acl = UuPipeSecurity.CreateAcl(identity.User!);
        var rules = acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier));
        check(acl.AreAccessRulesProtected && rules.Count == 1 && rules[0]!.IdentityReference.Equals(identity.User) && acl.GetOwner(typeof(System.Security.Principal.SecurityIdentifier))!.Equals(identity.User),"protected ACL grants only account SID, independent of token default owner");
        check(!rules[0]!.IdentityReference.Equals(new System.Security.Principal.SecurityIdentifier("S-1-1-0")),"no Everyone or alternate-user grant");
        bool invalidName = false; try { UuPipeSecurity.Name("shared"); } catch(ArgumentException) { invalidName = true; }
        check(invalidName && UuPipeSecurity.Name(new string('a',32)) == "StormHeroesLauncher.Uu."+new string('a',32),"random report-ID namespace preserved; broad names rejected");
        var logs = new List<string>();
        var cold = UuHelperResult.From("StartAndTray",report);
        var warm = UuHelperResult.From("TrayOnly",report with { LauncherValidated = false, UUStarted = false });
        var bytes = UuHelperCodec.Encode(cold);
        check(UuHelperCodec.Parse(bytes,0,"StartAndTray",logs.Add).Report == report,"StartAndTray versioned round-trip");
        check(UuHelperCodec.Parse(UuHelperCodec.Encode(warm),0,"TrayOnly",logs.Add).Valid,"TrayOnly uses same versioned schema");
        check(UuHelperCodec.Parse(Encoding.UTF8.GetBytes(" \r\n\uFEFF \t"+Encoding.UTF8.GetString(bytes)+" \r\n"),0,"StartAndTray",logs.Add).Valid,"UTF-8 BOM and whitespace accepted");
        foreach (var invalid in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("   "), bytes[..^3], Encoding.UTF8.GetBytes("junk"),Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("null"),new byte[] { 0xff } })
            check(!UuHelperCodec.Parse(invalid,0,"StartAndTray",logs.Add).Valid,"empty/malformed/truncated/schema-incomplete result rejected safely");
        check(!UuHelperCodec.Parse(UuHelperCodec.Encode(cold with { SchemaVersion = 2 }),0,"StartAndTray",logs.Add).Valid,"unknown schema version rejected");
        check(!UuHelperCodec.Parse(bytes,0,"TrayOnly",logs.Add).Valid,"operation mismatch rejected");
        check(!UuHelperCodec.Parse(bytes,26,"StartAndTray",logs.Add).Valid,"helper exit mismatch rejected");
        var failed = UuHelperResult.From("StartAndTray",report with { ExitCode = 20,LauncherValidated = false,WM_CLOSE = "NotPosted" });
        var failure = UuHelperCodec.Parse(UuHelperCodec.Encode(failed),20,"StartAndTray",logs.Add);
        check(failure.Valid && failure.Report?.ExitCode == 20,"nonzero helper result is parsed and retained as failure");
        check(!UuHelperCodec.Parse(UuHelperCodec.Encode(cold with { Success = false }),0,"StartAndTray",logs.Add).Valid,"inconsistent success rejected");
        check(!UuHelperCodec.Parse(UuHelperCodec.Encode(cold with { ProcessRunningAfter = false }),0,"StartAndTray",logs.Add).Valid,"success requires process-running evidence");
        using var stream = new MemoryStream(); UuHelperTransport.Write(stream,cold); byte[] frameBytes = stream.ToArray();
        using var fragmented = new FragmentedResultStream(frameBytes);
        var frame = await UuHelperTransport.ReadAsync(fragmented,default);
        check(frame.Status == "Complete" && frame.Payload.SequenceEqual(bytes),"fragmented reads reconstruct one frame");
        using var truncated = new MemoryStream(frameBytes[..^1]);
        check((await UuHelperTransport.ReadAsync(truncated,default)).Status == "TruncatedPayload","partial frame is diagnosed before JSON parsing");
        using var missing = new MemoryStream();
        check((await UuHelperTransport.ReadAsync(missing,default)).Status == "Missing","empty pipe is a Missing result, not JsonException");
        using var badHeader = new MemoryStream(Encoding.UTF8.GetBytes("not json text"));
        check((await UuHelperTransport.ReadAsync(badHeader,default)).Status == "BadMagic","unframed data rejected");
        foreach(var result in new[] {cold,warm})
        {
            string name = Guid.NewGuid().ToString("N");
            using var server = UuPipeSecurity.CreateServer(name);
            using var client = UuPipeSecurity.CreateClient(name);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var connected = server.WaitForConnectionAsync(deadline.Token); await client.ConnectAsync(deadline.Token); await connected;
            UuPipeSecurity.ValidateServer(client); UuPipeSecurity.ValidateClient(server,Environment.ProcessId);
            bool denied = false; try { UuPipeSecurity.ValidateClient(server,0); } catch (UnauthorizedAccessException) { denied = true; }
            check(denied,"unexpected client PID rejected");
            var writing = Task.Run(() => UuHelperTransport.Write(client,result)); // Concurrent writer remains open after the frame.
            var read = await UuHelperTransport.ReadAsync(server,deadline.Token); await writing;
            check(read.Status == "Complete" && UuHelperCodec.Parse(read.Payload,0,result.Operation,logs.Add).Valid,"same-process named pipe delivers "+result.Operation+" without waiting for EOF");
        }
        check(logs.Any(l => l.Contains("ResultLength=0") && l.Contains("ResultParse=Failed")) && logs.All(l => !l.Contains("junk")),"safe result metadata logged without payload contents");
        var host = new FakeTrayHost();
        var failedElevation = new PayloadElevation([],host,logger);
        var flow = new UuElevationFlow(logger,host,failedElevation,@"C:\Fake\uu_launcher.exe");
        var cli = new ResultTestCli(); var workflow = new HeroesBoostWorkflow(flow.EnsureAsync,cli,new UuCliOptions(),logger);
        try { await workflow.StartAsync(_ => {},default); } catch(InvalidOperationException) { }
        await flow.EnsureAsync(default);
        check(failedElevation.Calls == 1 && cli.Starts == 0 && cli.Queries == 0,"parse failure stops cold CLI flow without retry/UAC loop");
        host = new(); var goodElevation = new PayloadElevation(bytes,host,logger); flow = new(logger,host,goodElevation,@"C:\Fake\uu_launcher.exe");
        cli = new(); workflow = new(flow.EnsureAsync,cli,new UuCliOptions(),logger);
        check((await workflow.StartAsync(_ => {},default)).IsReady && cli.Starts == 1 && cli.Queries == 2 && goodElevation.Calls == 1,"successful cold result continues unchanged CLI readiness/start/confirmation flow");
    }
}
sealed class FragmentedResultStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(3,buffer.Length)],token);
}
sealed class PayloadElevation(byte[] bytes,FakeTrayHost host,AppLogger logger) : IUuElevation
{
    public int Calls;
    public Task<UuElevationResult> RunAsync(bool cold,string launcher,UuTrayTarget? target,CancellationToken token)
    {
        Calls++; var parsed = UuHelperCodec.Parse(bytes,0,"StartAndTray",logger.Write);
        if(parsed.Valid) host.Alive = true;
        return Task.FromResult(parsed.Valid ? new UuElevationResult(0,"Accepted",parsed.Report) : new UuElevationResult(-5,"InvalidHelperReport"));
    }
}
sealed class ResultTestCli : IUuCliService
{
    public int Starts,Queries;
    public Task<BoostOperationData> StartHeroesBoostAsync(CancellationToken token = default) { Starts++; return Task.FromResult(new BoostOperationData()); }
    public Task<HeroesBoostStatus> GetHeroesBoostStatusAsync(CancellationToken token = default) { Queries++; return Task.FromResult(new HeroesBoostStatus(true,"boosting",null,null,null,null,null,null)); }
    public Task<BoostOperationData> StopHeroesBoostAsync(CancellationToken token = default) => throw new InvalidOperationException("not used");
}
