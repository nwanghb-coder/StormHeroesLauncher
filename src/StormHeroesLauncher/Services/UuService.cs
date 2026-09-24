namespace StormHeroesLauncher.Services;
// The normal launcher never starts uu_launcher.exe itself. All cold-start elevation belongs to one helper.
public sealed class UuService
{
    private readonly UuElevationFlow flow;
    private readonly AppLogger logger;
    public UuService(AppLogger logger,string launcherPath)
    { this.logger = logger; flow = new(logger,new UuTrayNative(),new UuElevationRunner(logger),launcherPath); }
    public Task<string> DetectOrStartAsync(CancellationToken token) => Task.Run(async () => {
        try { return await flow.EnsureAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.Write($"UU elevation flow failed: {ex.GetType().Name}; no retry"); return "UU 加速器：启动失败；未重试 UAC"; }
    },token);
    public static Task<bool> IsRunningAsync(CancellationToken token = default) => Task.Run(() => new UuTrayNative().Running(),token);
}
