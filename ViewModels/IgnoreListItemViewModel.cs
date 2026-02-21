using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowedBorderless.ViewModels;

public partial class IgnoreListItemViewModel(string category, string processName, string description) : ObservableObject
{
  public string Category { get; } = category;
  public string ProcessName { get; } = processName;
  public string Description { get; } = description;

  [ObservableProperty] private bool _isForced;
}
