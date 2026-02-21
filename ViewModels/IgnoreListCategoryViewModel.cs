using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowedBorderless.ViewModels;

public partial class IgnoreListCategoryViewModel(string name) : ObservableObject
{
  public string Name => name;
  public ObservableCollection<IgnoreListItemViewModel> AllEntries { get; } = [];
  public ObservableCollection<IgnoreListItemViewModel> FilteredEntries { get; } = [];

  [ObservableProperty] private bool _isExpanded = true;
  [ObservableProperty] private bool _isVisible = true;

  public int TotalCount => AllEntries.Count;

  public void ApplyFilter(string search)
  {
    FilteredEntries.Clear();

    foreach (var entry in AllEntries)
    {
      var matches = string.IsNullOrWhiteSpace(search)
        || entry.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || entry.Description.Contains(search, StringComparison.OrdinalIgnoreCase);

      if (matches)
        FilteredEntries.Add(entry);
    }

    IsVisible = FilteredEntries.Count > 0;

    // Auto-expand categories with search matches, collapse empty ones
    if (!string.IsNullOrWhiteSpace(search))
      IsExpanded = IsVisible;
  }
}
