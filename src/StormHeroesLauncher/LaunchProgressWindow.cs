using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StormHeroesLauncher.Models;

namespace StormHeroesLauncher;

public sealed class LaunchProgressWindow : Window
{
    private readonly LaunchProgress progress;
    private readonly TextBlock status = new() { FontSize = 14, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock percentage = new() { Width = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 12, IsIndeterminate = false, VerticalAlignment = VerticalAlignment.Center };
    private bool closingAllowed, finishing, closed;
    public LaunchProgressSnapshot Displayed { get; private set; }

    public LaunchProgressWindow(LaunchProgress progress)
    {
        this.progress = progress;
        Displayed = progress.Current;
        Title = "StormHeroesLauncher";
        Icon = LauncherIcon.Load();
        Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, SystemColors.WindowBrushKey);
        SetResourceReference(ForegroundProperty, SystemColors.WindowTextBrushKey);
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "StormHeroesLauncher", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(status);
        var row = new Grid { Margin = new Thickness(0, 16, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(bar); Grid.SetColumn(percentage, 1); row.Children.Add(percentage);
        panel.Children.Add(row); Content = panel;
        Apply();
        progress.Changed += OnProgress;
    }
    private void OnProgress(LaunchProgressSnapshot _)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        // Always read the latest state when dispatched; queued notifications cannot paint an older stage.
        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Apply));
    }
    private void Apply()
    {
        if (closed) return;
        Displayed = progress.Current;
        status.Text = LaunchProgressText.For(Displayed.State);
        bar.Value = Displayed.Percentage;
        percentage.Text = $"{Displayed.Percentage}%";
    }
    public async Task FinishAsync()
    {
        Dispatcher.VerifyAccess();
        if (finishing || closed) return;
        finishing = true;
        Apply();
        // Only after the actual workflow finishes. Never delay launch work to animate progress.
        if (IsVisible) await Task.Delay(Displayed.State == LaunchState.GameReady ? 350 : 150);
        CloseForShutdown();
    }
    public void CloseForShutdown()
    {
        Dispatcher.VerifyAccess();
        if (closed) return;
        closingAllowed = true;
        Close();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        // Native close/Alt+F4 must not introduce a new workflow-cancellation or application-exit path.
        if (!closingAllowed) e.Cancel = true;
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        progress.Changed -= OnProgress;
        base.OnClosed(e);
    }
}
