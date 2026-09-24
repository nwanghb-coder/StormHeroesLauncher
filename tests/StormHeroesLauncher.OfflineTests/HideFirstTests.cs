using StormHeroesLauncher.Services;
using StormHeroesLauncher.WindowSupport;
public static class HideFirstTests
{
    public static async Task Run(AppLogger logger,Action<bool,string> check)
    {
        foreach(string app in new[]{"UU","Battle.net Chrome"})
        {
            var steps=new List<string>();bool visible=true;
            var a=HideTrayAttempt.Run(app,DateTimeOffset.UtcNow,()=>{steps.Add("visible identity");return visible;},()=>{steps.Add("hide");visible=false;return true;},()=>{steps.Add("identity after hide");return true;},()=>{steps.Add("WM_CLOSE");return true;},logger.Write);
            check(a.Posted && steps.SequenceEqual(new[]{"visible identity","hide","identity after hide","WM_CLOSE"}),app+" hides then revalidates identity then posts even when hidden");
            a.Observe(false,true);
        }
        int posts=0;
        var attempt=HideTrayAttempt.Run("fake",DateTimeOffset.UtcNow,()=>true,()=>false,()=>true,()=>{posts++;return true;},logger.Write);
        check(attempt.Posted && posts==1,"Hide failure still posts WM_CLOSE once");
        attempt=HideTrayAttempt.Run("fake",DateTimeOffset.UtcNow,()=>true,()=>true,()=>false,()=>{posts++;return true;},logger.Write);
        check(!attempt.Posted && posts==1,"changed identity after Hide blocks WM_CLOSE");
        attempt=HideTrayAttempt.Run("fake",DateTimeOffset.UtcNow,()=>false,()=>throw new Exception(),()=>true,()=>throw new Exception(),logger.Write);
        check(!attempt.Posted,"unrelated or invalid window receives neither action");
        attempt=HideTrayAttempt.Run("fake",DateTimeOffset.UtcNow,()=>true,()=>true,()=>true,()=>false,logger.Write);
        check(!attempt.Posted,"WM_CLOSE failure returns nonfatal result");
        var host=new FakeTrayHost();var elevation=new CloseFailureElevation(host);
        var flow=new UuElevationFlow(logger,host,elevation,"fake");
        check((await flow.EnsureAsync(default)).Contains("启动成功") && elevation.Calls==1,"UU confirmed running after tray message failure continues without another UAC");
    }
}
sealed class CloseFailureElevation(FakeTrayHost host):IUuElevation
{
    public int Calls;
    public Task<UuElevationResult> RunAsync(bool cold,string launcher,UuTrayTarget? target,CancellationToken token)
    {
        Calls++;host.Alive=true;
        return Task.FromResult(new UuElevationResult(23,"Accepted",new UuTrayReport(true,true,10,20,"UUMAINFORMV40","Failed",false,true,23)));
    }
}
