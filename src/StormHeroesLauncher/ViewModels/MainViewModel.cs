using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Models;
using StormHeroesLauncher.Services;

namespace StormHeroesLauncher.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppLogger logger = new();
    private readonly UuService uuService;
    private readonly IUuCliService? cli;
    private readonly HeroesBoostWorkflow? workflow;
    private readonly Dispatcher dispatcher;
    private CancellationTokenSource? operation;
    private bool disposed;
    private string status = "UU 加速器：尚未检测";
    private string boostStatus = "风暴英雄：尚未查询";
    private string boostDetails = "";
    private string logText = "";
    private bool isBusy;
    public string Status { get => status; private set { status = value; Changed(); } }
    public string BoostStatus { get => boostStatus; private set { boostStatus = value; Changed(); } }
    public string BoostDetails { get => boostDetails; private set { boostDetails = value; Changed(); } }
    public string LogText { get => logText; private set { logText = value; Changed(); } }
    public bool CanCheck => !isBusy && !disposed;
    public bool CanBoost => CanCheck && cli is not null;
    public bool CanCancel => isBusy && !disposed;
    public string LogPath => logger.LogPath;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        logger.MessageLogged += OnMessage;
        uuService = new UuService(logger, new SettingsStore().Load().UuLauncherPath);
        logger.Write("StormHeroesLauncher 程序启动。");
        logger.Write($"日志文件：{LogPath}");
        try
        {
            var options = new UuCliOptions { CliPath = new SettingsStore().Load().UuCliPath };
            cli = new UuCliService(options, logger, new CliProcessRunner());
            workflow = new HeroesBoostWorkflow(uuService.DetectOrStartAsync, cli, options, logger);
            logger.Write($"CLI 配置已加载：{options.CliPath}；启动程序不会自动执行加速。");
        }
        catch (Exception ex)
        {
            BoostStatus = "风暴英雄：CLI 配置无效，请检查 uu-cli.settings.json 并重新打开程序";
            logger.Write($"CLI 配置加载失败：{ex.GetType().Name}");
        }
    }

    public Task InitializeAsync() => RunAsync(async token =>
    {
        Status = await UuService.IsRunningAsync(token) ? "UU 加速器：已运行" : "UU 加速器：未运行";
        logger.Write(Status);
    }, boostOperation: false);

    public Task CheckAsync() => RunAsync(async token =>
    {
        Status = "UU 加速器：正在检测 / 等待启动…";
        Status = await uuService.DetectOrStartAsync(token);
    }, boostOperation: false);

    public Task StartBoostAsync() => RunAsync(async token =>
    {
        RequireCli();
        BoostDetails = "";
        BoostStatus = "风暴英雄：正在启动加速…";
        var progress = new Progress<string>(value => Status = value);
        Apply(await workflow!.StartAsync(value => ((IProgress<string>)progress).Report(value), token));
    }, boostOperation: true);

    public Task RefreshBoostAsync() => RunAsync(async token =>
    {
        RequireCli();
        Apply(await cli!.GetHeroesBoostStatusAsync(token));
    }, boostOperation: true);

    public Task StopBoostAsync() => RunAsync(async token =>
    {
        RequireCli();
        BoostDetails = "";
        BoostStatus = "风暴英雄：正在停止加速…";
        await cli!.StopHeroesBoostAsync(token);
        Apply(await workflow!.WaitStoppedAsync(token));
    }, boostOperation: true);

    private void RequireCli()
    {
        if (cli is null) throw new InvalidOperationException("UU CLI 配置无效。");
    }

    private void Apply(HeroesBoostStatus state)
    {
        BoostStatus = state.IsReady ? "风暴英雄：加速中" : state.Status switch
        {
            "starting" => "风暴英雄：正在启动加速…",
            "stopping" => "风暴英雄：正在停止加速…",
            _ => "风暴英雄：未加速"
        };
        BoostDetails = state.IsReady
            ? $"节点：{state.NodeName ?? "未知"}　模式：{state.NodeMode ?? "未知"}\n" +
              $"延迟：{state.Ping?.ToString("0.##") ?? "-"} ms　丢包率：{state.PacketLoss?.ToString("0.##") ?? "-"}%"
            : "";
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, bool boostOperation)
    {
        if (isBusy || disposed) return;
        isBusy = true;
        operation = new CancellationTokenSource();
        ControlsChanged();
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        {
            const string message = "操作已取消；请求可能已生效，请刷新状态。";
            if (boostOperation) { BoostStatus = "风暴英雄：" + message; BoostDetails = ""; }
            else Status = "UU 加速器：操作已取消";
            logger.Write(message + " 未关闭 UU，未自动发送 stop。");
        }
        catch (Exception ex)
        {
            var message = ex is UuCliException or TimeoutException or InvalidOperationException
                ? ex.Message : "操作失败，请查看日志。";
            if (boostOperation) { BoostStatus = "风暴英雄：失败，" + message; BoostDetails = ""; }
            else Status = "UU 加速器：操作失败，" + message;
            logger.Write($"操作失败：type={ex.GetType().Name}" +
                (ex is UuCliException failure ? $", kind={failure.Kind}, code={failure.ErrorCode ?? "-"}" : ""));
        }
        finally
        {
            operation.Dispose();
            operation = null;
            isBusy = false;
            ControlsChanged();
        }
    }

    public void Cancel() => operation?.Cancel();
    private void ControlsChanged()
    {
        Changed(nameof(CanCheck)); Changed(nameof(CanBoost)); Changed(nameof(CanCancel));
    }
    private void OnMessage(string message)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (disposed) return;
            LogText += message + Environment.NewLine;
            if (LogText.Length > 60000) LogText = LogText[^50000..];
        }));
    }
    private void Changed([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void Dispose()
    {
        disposed = true;
        operation?.Cancel();
        logger.MessageLogged -= OnMessage;
    }
}
