namespace StormHeroesLauncher.WindowSupport;
public sealed class HideTrayAttempt(string context,Action<string> log)
{
    public DateTimeOffset HideRequested {get;private set;}
    public DateTimeOffset? CloseRequested {get;private set;}
    public bool Posted {get;private set;}
    private bool confirmed;
    public static HideTrayAttempt Run(string context,DateTimeOffset detected,Func<bool> validVisible,Func<bool> hide,Func<bool> validIdentity,Func<bool> post,Action<string> log)
    {
        var attempt=new HideTrayAttempt(context,log);
        if(!validVisible()) {log(context+" Action=HideThenWM_CLOSE Result=IdentityChanged");return attempt;}
        attempt.HideRequested=DateTimeOffset.UtcNow;
        bool hidden=hide();
        // Visibility may now be false: identity, not visibility, authorizes the native tray message.
        if(validIdentity()) {attempt.CloseRequested=DateTimeOffset.UtcNow;attempt.Posted=post();}
        log($"{context} Action=HideThenWM_CLOSE HideRequestedT={attempt.HideRequested:O} HideAccepted={hidden} WMCloseRequestedT={attempt.CloseRequested?.ToString("O") ?? "NotSent_IdentityChanged"} WMClosePosted={attempt.Posted} FirstDetectedToHideMs={(attempt.HideRequested-detected).TotalMilliseconds:F2} HideToWMCloseMs={(attempt.CloseRequested.HasValue ? (attempt.CloseRequested.Value-attempt.HideRequested).TotalMilliseconds.ToString("F2") : "Unknown")} HideConfirmedT=Unknown TrayConfirmedT=Unknown");
        return attempt;
    }
    public void Observe(bool visible,bool alive)
    {
        if(confirmed || visible || HideRequested == default) return;
        confirmed=true;var time=DateTimeOffset.UtcNow;
        log($"{context} HideConfirmedT={time:O} HideLatencyMs={(time-HideRequested).TotalMilliseconds:F2} TrayConfirmedT={(Posted && alive ? time.ToString("O") : "Unknown")} WMCloseToTrayConfirmMs={(Posted && alive && CloseRequested.HasValue ? (time-CloseRequested.Value).TotalMilliseconds.ToString("F2") : "Unknown")} ConfirmationBasis=WM_CLOSEAccepted_HiddenAndProcessAlive_Proxy_NotTrayIcon ProcessRunningAfter={alive} VisibleAfter={visible}");
    }
}
