using System.Windows;
using System.Windows.Controls;
namespace StormHeroesLauncher;
public sealed class ShortcutImportWindow:Window
{
    public ShortcutImportWindow(string message)
    {
        Icon=LauncherIcon.Load();
        Title="StormHeroesLauncher";Width=380;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel {Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock {Text=message,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,16)});
        var ok=new Button {Content="确定",MinWidth=80,Padding=new Thickness(14,5,14,5),HorizontalAlignment=HorizontalAlignment.Center,IsDefault=true,IsCancel=true};
        ok.Click+=(_,_)=>Close();panel.Children.Add(ok);
    }
}
