using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowedBorderless.Models;

public partial class WindowInfo(nint handle, string processName, string windowTitle) : ObservableObject
{
  public nint Handle { get; } = handle;
  public string ProcessName { get; } = processName;
  public Bitmap? Icon { get; init; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(DisplayName))]
  private string _windowTitle = windowTitle;

  public string DisplayName => $"{ProcessName} - {WindowTitle}";
}

public record WindowOriginalState(
  RECT OriginalRect,
  bool WasMaximized,
  bool WasMinimized);

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
  public int Left;
  public int Top;
  public int Right;
  public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
  public uint cbSize;
  public RECT rcMonitor;
  public RECT rcWork;
  public uint dwFlags;
}
