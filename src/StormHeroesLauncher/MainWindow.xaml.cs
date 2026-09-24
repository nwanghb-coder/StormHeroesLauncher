using System.Windows;
using StormHeroesLauncher.ViewModels;

namespace StormHeroesLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainViewModel(Dispatcher);
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closed += (_, _) => viewModel.Dispose();
    }
    private async void CheckUu_Click(object sender, RoutedEventArgs e) => await viewModel.CheckAsync();
    private async void StartBoost_Click(object sender, RoutedEventArgs e) => await viewModel.StartBoostAsync();
    private async void RefreshBoost_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshBoostAsync();
    private async void StopBoost_Click(object sender, RoutedEventArgs e) => await viewModel.StopBoostAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => viewModel.Cancel();
}
