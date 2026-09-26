using System.Windows;
using System.Windows.Controls;
using StormHeroesLauncher;
using StormHeroesLauncher.Services;
public static class AboutSafetyTests
{
    public static void Run(Action<bool,string> check)
    {
        check(AboutSafetyContent.Publisher=="阿黄" && AboutSafetyContent.Email=="441649289@qq.com","About publisher and contact exact");
        check((AboutSafetyContent.Version == "0.3.0-dev.2" || AboutSafetyContent.Version.StartsWith("0.3.0-dev.2+", StringComparison.Ordinal)) && System.Text.RegularExpressions.Regex.IsMatch(AboutSafetyContent.BuildId, @"\A0\.3\.0-dev\.2-[0-9a-f]{12}\z"),"version from assembly; populated build metadata and deterministic module ID");
        string support=AboutSafetyContent.SupportInfo;
        check(support.Split('\n').Length==5 && support.Contains("Architecture: x64") && !support.Contains('\\') && !support.Contains("User",StringComparison.OrdinalIgnoreCase) && !support.Contains("IP address",StringComparison.OrdinalIgnoreCase),"support block contains only allowlisted version/build/architecture/OS fields");
        check(AboutSafetyContent.LogsDirectory==System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StormHeroesLauncher","Logs"),"logs action fixed to launcher-owned folder");
        var uri=new Uri(AboutSafetyContent.FeedbackUri);
        check(uri.Scheme=="mailto" && AboutSafetyContent.FeedbackUri.StartsWith("mailto:441649289@qq.com?subject=") && !uri.Query.Contains("attach") && !uri.Query.Contains("body"),"feedback uses fixed recipient and subject; no files or automatic content upload");
        check(AboutSafetyContent.Sections.Select(s=>s.Heading).SequenceEqual(new[]{"关于","主要功能","安全与隐私","技术实现","反馈与诊断","独立第三方工具声明"}),"six static known content sections");
        string safety=AboutSafetyContent.Sections.Single(s=>s.Heading=="安全与隐私").Text;
        check(safety.Contains("额外内容也可能被记录") && !safety.Contains("100%") && !safety.Contains("绝对安全"),"safety copy acknowledges logging limits without guarantees");
        Exception? error=null;bool quiet=false,copied=false,feedback=false,logs=false;
        var thread=new Thread(()=>{
            try
            {
                var fake=new FakeAboutActions();var window=new AboutSafetyWindow(fake);
                quiet=fake.Calls==0;
                IEnumerable<DependencyObject> Walk(DependencyObject node)
                {yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var item in Walk(child))yield return item;}
                var buttons=Walk(window).OfType<Button>().ToArray();
                buttons.Single(b=>b.Name=="CopyVersion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));copied=fake.Copied==support;
                buttons.Single(b=>b.Name=="Feedback").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));feedback=fake.FeedbackCalls==1;
                buttons.Single(b=>b.Name=="OpenLogs").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));logs=fake.LogCalls==1;
                window.Close();
            }catch(Exception ex){error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        check(error==null && quiet,"About construction has no launch/UAC/configuration actions; window not shown");
        check(error==null && copied && feedback && logs,"About buttons delegate only explicit requested actions to fakes");
    }
}
sealed class FakeAboutActions:IAboutActions
{
    public int Calls,FeedbackCalls,LogCalls;public string Copied="";
    public void Copy(string text){Calls++;Copied=text;}
    public void OpenLogs(){Calls++;LogCalls++;}
    public void Feedback(){Calls++;FeedbackCalls++;}
}
