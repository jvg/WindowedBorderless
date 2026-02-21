using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using WindowedBorderless.Filtering;
using WindowedBorderless.Services;
using WindowedBorderless.ViewModels;
using WindowedBorderless.Views;

namespace WindowedBorderless;

public partial class App : Application
{
  private MainWindowViewModel? _mainViewModel;

  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
  }

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      var settings = SettingsService.Load();

      // Apply saved theme before the window is shown
      RequestedThemeVariant = settings.Theme switch
      {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
      };

      var windowManager = new WindowManagerService();
      var ignoreList = IgnoreList.LoadDefault();
      _mainViewModel = new MainWindowViewModel(windowManager, ignoreList, settings.Theme);

      desktop.MainWindow = new MainWindow { DataContext = _mainViewModel };
      desktop.ShutdownRequested += OnShutdownRequested;

      _ = _mainViewModel.CheckForUpdatesAsync();
    }

    base.OnFrameworkInitializationCompleted();
  }

  private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
  {
    _mainViewModel?.Shutdown();
  }
}