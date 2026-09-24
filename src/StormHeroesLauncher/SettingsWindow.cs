using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StormHeroesLauncher.Configuration;
using StormHeroesLauncher.Services;
namespace StormHeroesLauncher;
public sealed class SettingsWindow : Window
{
    private readonly TextBox[] paths = Enumerable.Range(0, 4).Select(_ => new TextBox { MinWidth = 430, Margin = new Thickness(0, 3, 8, 3) }).ToArray();
    private readonly ComboBox mode = new() { ItemsSource = Enum.GetValues<BattleNetWindowMode>(), Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock cliStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    public SettingsWindow(LauncherSettings settings, SettingsStore store, PathDiscovery discovery, UuCliPreparation preparation, AppLogger logger, string? error)
    {
        Icon=LauncherIcon.Load();
        Title = "StormHeroesLauncher 0.2.0-friend-test — 设置"; Width = 760; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "请先在 UU 和战网完成登录并启用记住/自动登录。保存后退出，不会启动游戏。", TextWrapping = TextWrapping.Wrap });
        var labels = new[] { "UU 启动器 (uu_launcher.exe)", "高级：官方组件路径（通常无需更改）", "Battle.net.exe", "HeroesSwitcher_x64.exe (Support64)" };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var row = new DockPanel(); var browse = new Button { Content = "浏览…", Padding = new Thickness(10, 3, 10, 3) };
            browse.Click += (_, _) => { var picker = new OpenFileDialog { Filter = "可执行文件 (*.exe)|*.exe", CheckFileExists = true }; if (picker.ShowDialog(this) == true) paths[index].Text = picker.FileName; };
            DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(paths[i]);
            if (i == 1) { panel.Children.Add(cliStatus); panel.Children.Add(new Expander { Header = labels[i], Content = row, IsExpanded = false }); }
            else { panel.Children.Add(new TextBlock { Text = labels[i], Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(row); }
        }
        panel.Children.Add(new TextBlock { Text = "战网窗口：Minimized 最小化（默认）；Normal 保持现状" }); panel.Children.Add(mode); panel.Children.Add(status);
        async Task PrepareAsync(bool save)
        {
            panel.IsEnabled = false;
            try
            {
                var current = Read();
                var result = await Task.Run(() => { var found = discovery.Discover(current); var cli = preparation.Resolve(found.UuCliPath, found.UuLauncherPath); return (Settings: found with { UuCliPath = cli.Path }, Cli: cli); });
                Populate(result.Settings); cliStatus.Text = result.Cli.Message;
                status.Text = result.Cli.Success ? string.Join("\n", result.Settings.Validate()) : result.Cli.Message;
                if (save && result.Cli.Success) { result.Settings.RequireValid(); store.Save(result.Settings); logger.WriteOperation("保存设置", result.Settings); Close(); }
            }
            catch (Exception ex) { status.Text = "准备或保存未完成：" + ex.Message; }
            finally { panel.IsEnabled = true; }
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var detect = new Button { Content = "自动检测 / 准备组件", Margin = new Thickness(0, 0, 15, 0), Padding = new Thickness(12, 5, 12, 5) };
        detect.Click += async (_, _) => await PrepareAsync(false);
        var save = new Button { Content = "保存并退出", Padding = new Thickness(12, 5, 12, 5) };
        save.Click += async (_, _) => await PrepareAsync(true);
                actions.Children.Add(detect); actions.Children.Add(save);
        var about=new Button {Content="关于 / 安全",Padding=new Thickness(12,5,12,5),Margin=new Thickness(15,0,0,0)};
        about.Click+=(_,_)=>new AboutSafetyWindow {Owner=this}.ShowDialog();
        actions.Children.Add(about);panel.Children.Add(actions);
        Populate(settings); status.Text = string.Join("\n", new[] { error ?? "" }.Concat(settings.Validate()));
    }
    private void Populate(LauncherSettings value) { paths[0].Text = value.UuLauncherPath; paths[1].Text = value.UuCliPath; paths[2].Text = value.BattleNetPath; paths[3].Text = value.HeroesSwitcherPath; mode.SelectedItem = value.BattleNetWindowMode; cliStatus.Text = string.IsNullOrEmpty(value.UuCliPath) ? "未找到网易 UU 官方 CLI 组件" : "UU CLI：已自动检测"; }
    private LauncherSettings Read() => new() { UuLauncherPath = paths[0].Text.Trim(), UuCliPath = paths[1].Text.Trim(), BattleNetPath = paths[2].Text.Trim(), HeroesSwitcherPath = paths[3].Text.Trim(), BattleNetWindowMode = mode.SelectedItem is BattleNetWindowMode selected ? selected : BattleNetWindowMode.Minimized };
}
