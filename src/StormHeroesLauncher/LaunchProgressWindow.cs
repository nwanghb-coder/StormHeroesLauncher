using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using StormHeroesLauncher.Models;

namespace StormHeroesLauncher;

public sealed class LaunchProgressWindow : Window
{
    private readonly LaunchProgress progress;
    private readonly TextBlock status = new() { FontSize = 11, TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock percentage = new() { FontSize = 13, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 12, IsIndeterminate = false, VerticalAlignment = VerticalAlignment.Center };
    private bool closingAllowed, finishing, closed;
    private readonly Action? cancel;
    public LaunchProgressSnapshot Displayed { get; private set; }

    public LaunchProgressWindow(LaunchProgress progress, Action? cancel = null)
    {
        this.progress = progress;
        this.cancel = cancel;
        Displayed = progress.Current;
        Title = "HOSLauncher";
        Icon = LauncherIcon.Load();
        Width = 340; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, SystemColors.WindowBrushKey);
        SetResourceReference(ForegroundProperty, SystemColors.WindowTextBrushKey);
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "HOSLauncher", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var row = new Grid { Margin = new Thickness(0, 10, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(bar); Grid.SetColumn(percentage, 1); row.Children.Add(percentage);
        panel.Children.Add(row); panel.Children.Add(status); Content = panel;
        Apply();
        progress.Changed += OnProgress;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && cancel != null && !closed &&
            progress.Current.State is not (LaunchState.GameReady or LaunchState.Failed or LaunchState.Cancelled))
        {
            e.Handled = true;
            progress.Report(LaunchState.Cancelled);
            cancel();
            _ = FinishAsync();
        }
        base.OnPreviewKeyDown(e);
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
