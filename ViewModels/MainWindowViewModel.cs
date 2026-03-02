using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;
using WindowedBorderless.Filtering;
using WindowedBorderless.Models;
using WindowedBorderless.Services;

namespace WindowedBorderless.ViewModels;

public partial class MainWindowViewModel(
  WindowManagerService windowManager,
  IgnoreList ignoreList,
  string initialTheme = "System") : ObservableObject
{
  public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];

  [ObservableProperty] private string _selectedTheme = initialTheme;

  [ObservableProperty] private bool _showAll;

  [ObservableProperty] private bool _isUpdateReady;

  private UpdateManager? _updateManager;
  private UpdateInfo? _pendingUpdate;

  [ObservableProperty] private bool _showIgnoreListView;

  [ObservableProperty] private string _ignoreListSearch = "";

  public ObservableCollection<WindowInfo> AvailableWindows { get; } = [];
  public ObservableCollection<WindowInfo> BorderlessWindows { get; } = [];
  public ObservableCollection<IgnoreListCategoryViewModel> IgnoreListCategories { get; } = [];
  public ObservableCollection<string> UserIgnoredEntries { get; } = [];

  private readonly HashSet<string> _ignoredDisplayNames = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _forcedProcessNames = new(StringComparer.OrdinalIgnoreCase);

  private List<RememberedWindow> _rememberedWindows = [];
  private bool _initializing;

  private DispatcherTimer? _debounceTimer;
  private DispatcherTimer? _fallbackTimer;

  public async Task CheckForUpdatesAsync()
  {
    try
    {
      _updateManager = new UpdateManager(new GithubSource("https://github.com/jvg/WindowedBorderless", null, false));
      _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
      if (_pendingUpdate is not null)
      {
        await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
        IsUpdateReady = true;
      }
    }
    catch
    {
      // Silently ignore — app must always start regardless of network
    }
  }

  public void RestartToUpdate()
  {
    if (_pendingUpdate is not null)
      _updateManager?.ApplyUpdatesAndRestart(_pendingUpdate);
  }

  partial void OnSelectedThemeChanged(string value)
  {
    if (Application.Current is not { } app) return;

    app.RequestedThemeVariant = value switch
    {
      "Light" => ThemeVariant.Light,
      "Dark" => ThemeVariant.Dark,
      _ => ThemeVariant.Default
    };

    SaveSettings();
  }

  partial void OnShowAllChanged(bool value)
  {
    RefreshWindowList();
  }

  partial void OnIgnoreListSearchChanged(string value)
  {
    foreach (var category in IgnoreListCategories)
      category.ApplyFilter(value);
  }

  public void Initialize()
  {
    _initializing = true;

    var settings = SettingsService.Load();

    _rememberedWindows = settings.RememberedWindows.ToList();

    foreach (var name in settings.IgnoredDisplayNames)
      _ignoredDisplayNames.Add(name);

    foreach (var name in settings.ForcedProcessNames)
      _forcedProcessNames.Add(name);

    PopulateIgnoreListCollections();

    // First refresh unfiltered so AutoSelectRemembered can find processes on the blocklist
    RefreshWindowList(true);
    AutoSelectRemembered(_rememberedWindows);
    RefreshWindowList();

    // Add placeholders for remembered windows whose process isn't running
    foreach (var rw in _rememberedWindows)
    {
      if (!BorderlessWindows.Any(w => string.Equals(w.ProcessName, rw.ProcessName, StringComparison.OrdinalIgnoreCase)))
        BorderlessWindows.Add(new WindowInfo(0, rw.ProcessName, rw.TitleHint));
    }

    _initializing = false;
    SaveSettings();

    // Debounce timer: coalesces rapid WinEvent notifications into a single refresh
    _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    _debounceTimer.Tick += (_, _) =>
    {
      _debounceTimer.Stop();
      RefreshWindowList();
    };

    // Silent fallback poll as a safety net
    _fallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
    _fallbackTimer.Tick += (_, _) => RefreshWindowList();
    _fallbackTimer.Start();

    // Event-driven window change detection
    windowManager.StartWatching(OnWindowChanged);
  }

  public void Shutdown()
  {
    windowManager.StopWatching();
    _debounceTimer?.Stop();
    _fallbackTimer?.Stop();

    SaveSettings();
  }

  private void SaveSettings()
  {
    if (_initializing) return;

    var remembered = BorderlessWindows
      .Select(w => new RememberedWindow
      {
        ProcessName = w.ProcessName,
        TitleHint = w.WindowTitle
      })
      .ToList();

    // Preserve entries for processes that haven't appeared yet
    foreach (var rw in _rememberedWindows)
    {
      if (!remembered.Any(r => string.Equals(r.ProcessName, rw.ProcessName, StringComparison.OrdinalIgnoreCase)))
        remembered.Add(rw);
    }

    var settings = new Settings
    {
      RememberedWindows = remembered,
      IgnoredDisplayNames = _ignoredDisplayNames.ToList(),
      ForcedProcessNames = _forcedProcessNames.ToList(),
      Theme = SelectedTheme
    };
    SettingsService.Save(settings);
  }

  private void OnWindowChanged()
  {
    // Reset the debounce timer — the refresh fires 300ms after the last event
    _debounceTimer?.Stop();
    _debounceTimer?.Start();
  }

  public void MakeSelectedBorderless(IList<WindowInfo> selected)
  {
    foreach (var window in selected.ToList())
    {
      windowManager.MakeBorderless(window.Handle);
      AvailableWindows.Remove(window);
      BorderlessWindows.Add(window);
    }

    SaveSettings();
  }

  public void RestoreSelected(IList<WindowInfo> selected)
  {
    foreach (var window in selected.ToList())
    {
      if (!window.IsPlaceholder)
      {
        windowManager.RestoreWindow(window.Handle);
        AvailableWindows.Add(window);
      }

      BorderlessWindows.Remove(window);
      _rememberedWindows.RemoveAll(r =>
        string.Equals(r.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    SaveSettings();
  }

  public void IgnoreProcess(WindowInfo window)
  {
    _ignoredDisplayNames.Add(window.DisplayName);
    SyncUserIgnoredEntries();
    RefreshWindowList();
    SaveSettings();
  }

  public void UnignoreProcess(WindowInfo window)
  {
    _ignoredDisplayNames.Remove(window.DisplayName);
    SyncUserIgnoredEntries();
    RefreshWindowList();
    SaveSettings();
  }

  public bool IsUserIgnored(string displayName)
  {
    return _ignoredDisplayNames.Contains(displayName);
  }

  public void ToggleForced(string processName)
  {
    if (!_forcedProcessNames.Remove(processName))
      _forcedProcessNames.Add(processName);

    var isForced = _forcedProcessNames.Contains(processName);

    var item = IgnoreListCategories.SelectMany(c => c.AllEntries)
      .FirstOrDefault(e => string.Equals(e.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
    if (item is not null)
      item.IsForced = isForced;

    RefreshWindowList();
    SaveSettings();
  }

  public void RemoveUserIgnore(string displayName)
  {
    _ignoredDisplayNames.Remove(displayName);
    SyncUserIgnoredEntries();
    RefreshWindowList();
    SaveSettings();
  }

  private void PopulateIgnoreListCollections()
  {
    IgnoreListCategories.Clear();

    var grouped = ignoreList.Entries
      .OrderBy(e => e.Category)
      .ThenBy(e => e.ProcessName)
      .GroupBy(e => e.Category);

    foreach (var group in grouped)
    {
      var categoryVm = new IgnoreListCategoryViewModel(group.Key);

      foreach (var entry in group)
      {
        var vm = new IgnoreListItemViewModel(entry.Category, entry.ProcessName, entry.Description)
        {
          IsForced = _forcedProcessNames.Contains(entry.ProcessName)
        };
        categoryVm.AllEntries.Add(vm);
        categoryVm.FilteredEntries.Add(vm);
      }

      IgnoreListCategories.Add(categoryVm);
    }

    SyncUserIgnoredEntries();
  }

  private void SyncUserIgnoredEntries()
  {
    UserIgnoredEntries.Clear();
    foreach (var name in _ignoredDisplayNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
      UserIgnoredEntries.Add(name);
  }

  private void RefreshWindowList(bool bypassFilter = false)
  {
    var borderlessHandles = BorderlessWindows.Select(w => w.Handle).ToHashSet();
    var freshWindows = windowManager.GetAvailableWindows(borderlessHandles);

    if (!bypassFilter && !ShowAll)
      freshWindows = freshWindows
        .Where(w => !ignoreList.IsIgnored(w.ProcessName, _forcedProcessNames)
                    && !_ignoredDisplayNames.Contains(w.DisplayName))
        .ToList();

    var freshHandles = freshWindows.Select(w => w.Handle).ToHashSet();

    // Remove windows that disappeared or are now filtered
    for (var i = AvailableWindows.Count - 1; i >= 0; i--)
      if (!freshHandles.Contains(AvailableWindows[i].Handle))
        AvailableWindows.RemoveAt(i);

    // Add newly appeared windows (existing items + their selection are preserved)
    var existingHandles = AvailableWindows.Select(w => w.Handle).ToHashSet();
    foreach (var window in freshWindows)
      if (!existingHandles.Contains(window.Handle))
        AvailableWindows.Add(window);

    // Refresh titles for available windows from the fresh enumeration
    var freshTitleByHandle = freshWindows.ToDictionary(w => w.Handle, w => w.WindowTitle);
    foreach (var window in AvailableWindows)
      if (freshTitleByHandle.TryGetValue(window.Handle, out var freshTitle)
          && !string.IsNullOrEmpty(freshTitle)
          && freshTitle != window.WindowTitle)
        window.WindowTitle = freshTitle;

    // Refresh titles for borderless windows via live GetWindowText
    // and detect closed windows — convert them back to placeholders
    for (var i = BorderlessWindows.Count - 1; i >= 0; i--)
    {
      var window = BorderlessWindows[i];
      if (window.IsPlaceholder) continue;

      var liveTitle = windowManager.GetCurrentTitle(window.Handle);
      if (!string.IsNullOrEmpty(liveTitle))
      {
        if (liveTitle != window.WindowTitle)
          window.WindowTitle = liveTitle;
      }
      else
      {
        // Handle is no longer valid — process closed. Convert to placeholder.
        var placeholder = new WindowInfo(0, window.ProcessName, window.WindowTitle);
        BorderlessWindows[i] = placeholder;
        _rememberedWindows.Add(new RememberedWindow
        {
          ProcessName = window.ProcessName,
          TitleHint = window.WindowTitle
        });
      }
    }

    AutoSelectNewlyAppeared();
  }

  private void AutoSelectNewlyAppeared()
  {
    if (_rememberedWindows.Count == 0) return;

    var toSelect = AvailableWindows
      .Where(w => _rememberedWindows.Any(r =>
        string.Equals(r.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(r.TitleHint)
            || w.WindowTitle.Contains(r.TitleHint, StringComparison.OrdinalIgnoreCase)
            || r.TitleHint.Contains(w.WindowTitle, StringComparison.OrdinalIgnoreCase))))
      .ToList();

    if (toSelect.Count == 0) return;

    // Remove matched entries from the watchlist before making borderless
    foreach (var w in toSelect)
    {
      _rememberedWindows.RemoveAll(r =>
        string.Equals(r.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase));

      // Remove matching placeholder from BorderlessWindows
      var placeholder = BorderlessWindows.FirstOrDefault(b =>
        b.IsPlaceholder && string.Equals(b.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase));
      if (placeholder is not null)
        BorderlessWindows.Remove(placeholder);
    }

    MakeSelectedBorderless(toSelect);
  }

  private void AutoSelectRemembered(List<RememberedWindow> remembered)
  {
    if (remembered.Count == 0) return;

    var toSelect = AvailableWindows
      .Where(w => remembered.Any(r =>
        string.Equals(r.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(r.TitleHint)
            || w.WindowTitle.Contains(r.TitleHint, StringComparison.OrdinalIgnoreCase)
            || r.TitleHint.Contains(w.WindowTitle, StringComparison.OrdinalIgnoreCase))))
      .ToList();

    MakeSelectedBorderless(toSelect);
  }
}
