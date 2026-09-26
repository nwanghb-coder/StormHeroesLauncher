using System.Windows;
using System.Windows.Controls;
using StormHeroesLauncher.Services;
namespace StormHeroesLauncher;
public sealed class AboutSafetyWindow:Window
{
    public AboutSafetyWindow(IAboutActions? actions=null)
    {
        actions ??=new WindowsAboutActions();
        Icon=LauncherIcon.Load();
        Title="关于 / 安全 — HOSLauncher";Width=650;Height=720;MinWidth=430;MinHeight=400;
        MaxHeight=SystemParameters.WorkArea.Height*0.9;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel {Margin=new Thickness(24)};
        Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
        foreach(var (heading,text) in AboutSafetyContent.Sections)
        {
            panel.Children.Add(new TextBlock {Text=heading,FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,8)});
            panel.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,LineHeight=22});
            if(heading=="关于")
            {
                panel.Children.Add(new TextBlock {Text=$"程序版本：{AboutSafetyContent.Version}\nBuild ID：{AboutSafetyContent.BuildId}",Margin=new Thickness(0,10,0,6),TextWrapping=TextWrapping.Wrap});
                panel.Children.Add(new TextBlock {Text="联系邮箱"});
                panel.Children.Add(new TextBox {Text=AboutSafetyContent.Email,IsReadOnly=true,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent,Padding=new Thickness(0,4,0,4)});
            }
            if(heading=="反馈与诊断")
            {
                var row=new WrapPanel {Margin=new Thickness(0,12,0,0)};panel.Children.Add(row);panel.Children.Add(status);
                void Add(string caption,string name,Action action,string success,string failure)
                {
                    var button=new Button {Name=name,Content=caption,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,6)};
                    button.Click+=(_,_)=>{try {action();status.Text=success;} catch {status.Text=failure;}};row.Children.Add(button);
                }
                Add("复制版本信息","CopyVersion",()=>actions.Copy(AboutSafetyContent.SupportInfo),"已复制","暂时无法访问剪贴板，请稍后重试。");
                Add("打开日志目录","OpenLogs",actions.OpenLogs,"已请求打开日志目录。","无法打开日志目录，请手动查看 %LOCALAPPDATA%\\StormHeroesLauncher\\Logs。");
                Add("反馈问题","Feedback",actions.Feedback,"已请求打开邮件客户端；如未打开，请复制邮箱："+AboutSafetyContent.Email,"无法打开邮件客户端，请复制邮箱："+AboutSafetyContent.Email);
            }
        }
        var close=new Button {Content="关闭",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(18,5,18,5),Margin=new Thickness(0,20,0,0)};
        close.Click+=(_,_)=>Close();panel.Children.Add(close);
    }
}
