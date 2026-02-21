using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WindowedBorderless.Models;
using WindowedBorderless.ViewModels;

namespace WindowedBorderless.Views;

public partial class MainWindow : Window
{
  private WindowInfo? _contextTarget;

  public MainWindow()
  {
    InitializeComponent();
    Loaded += OnLoaded;
  }

  private void OnLoaded(object? sender, RoutedEventArgs e)
  {
    // Tunneling handler fires before the ContextMenu opens, letting us capture the right-clicked item
    AvailableList.AddHandler(
      PointerPressedEvent,
      OnAvailableListPointerPressed,
      RoutingStrategies.Tunnel);

    if (DataContext is MainWindowViewModel vm)
      vm.Initialize();
  }

  private void OnAvailableListPointerPressed(object? sender, PointerPressedEventArgs e)
  {
    _contextTarget = null;

    if (!e.GetCurrentPoint(AvailableList).Properties.IsRightButtonPressed)
      return;

    // Walk up from the event source to find the WindowInfo DataContext
    var current = e.Source as Control;
    while (current is not null)
    {
      if (current.DataContext is WindowInfo window)
      {
        _contextTarget = window;
        return;
      }

      current = current.Parent as Control;
    }
  }

  private void OnMakeBorderlessClick(object? sender, RoutedEventArgs e)
  {
    if (DataContext is MainWindowViewModel vm)
    {
      var selected = AvailableList.SelectedItems?.Cast<WindowInfo>().ToList() ?? [];
      vm.MakeSelectedBorderless(selected);
    }
  }

  private void OnRestoreClick(object? sender, RoutedEventArgs e)
  {
    if (DataContext is MainWindowViewModel vm)
    {
      var selected = BorderlessList.SelectedItems?.Cast<WindowInfo>().ToList() ?? [];
      vm.RestoreSelected(selected);
    }
  }

  private void OnAvailableContextMenuOpening(object? sender, CancelEventArgs e)
  {
    if (sender is not ContextMenu menu
        || _contextTarget is not { } window
        || DataContext is not MainWindowViewModel vm)
    {
      e.Cancel = true;
      return;
    }

    var isIgnored = vm.IsUserIgnored(window.DisplayName);

    var ignoreItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "IgnoreMenuItem");
    var unignoreItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "UnignoreMenuItem");

    if (ignoreItem is not null)
    {
      ignoreItem.Header = $"Ignore {window.DisplayName}";
      ignoreItem.IsVisible = !isIgnored;
      ignoreItem.Tag = window;
    }

    if (unignoreItem is not null)
    {
      unignoreItem.Header = $"Unignore {window.DisplayName}";
      unignoreItem.IsVisible = vm.ShowAll && isIgnored;
      unignoreItem.Tag = window;
    }

    // Cancel if nothing to show
    if (ignoreItem is { IsVisible: false } && unignoreItem is { IsVisible: false })
      e.Cancel = true;
  }

  private void OnIgnoreClick(object? sender, RoutedEventArgs e)
  {
    if (sender is MenuItem { Tag: WindowInfo window }
        && DataContext is MainWindowViewModel vm)
      vm.IgnoreProcess(window);
  }

  private void OnUnignoreClick(object? sender, RoutedEventArgs e)
  {
    if (sender is MenuItem { Tag: WindowInfo window }
        && DataContext is MainWindowViewModel vm)
      vm.UnignoreProcess(window);
  }

  private void OnToggleIgnoreListView(object? sender, RoutedEventArgs e)
  {
    if (DataContext is MainWindowViewModel vm)
      vm.ShowIgnoreListView = !vm.ShowIgnoreListView;
  }

  private void OnToggleForcedClick(object? sender, RoutedEventArgs e)
  {
    if (sender is ToggleSwitch { DataContext: IgnoreListItemViewModel item }
        && DataContext is MainWindowViewModel vm)
      vm.ToggleForced(item.ProcessName);
  }

  private void OnRemoveUserIgnoreClick(object? sender, RoutedEventArgs e)
  {
    if (sender is Button { Tag: string processName }
        && DataContext is MainWindowViewModel vm)
      vm.RemoveUserIgnore(processName);
  }

  private void OnRestartToUpdateClick(object? sender, RoutedEventArgs e)
  {
    (DataContext as MainWindowViewModel)?.RestartToUpdate();
  }

  private void OnOpenAppDataClick(object? sender, RoutedEventArgs e)
  {
    var folder = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "WindowedBorderless");
    Directory.CreateDirectory(folder);
    Process.Start("explorer.exe", folder);
  }
}
