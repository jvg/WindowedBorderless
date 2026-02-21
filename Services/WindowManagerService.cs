using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowedBorderless.Models;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;

namespace WindowedBorderless.Services;

public partial class WindowManagerService
{
  // Window style constants
  private const int GWL_STYLE = -16;
  private const int GWL_EXSTYLE = -20;
  private const int WS_CAPTION = 0x00C00000;
  private const int WS_THICKFRAME = 0x00040000;
  private const int WS_MINIMIZEBOX = 0x00020000;
  private const int WS_MAXIMIZEBOX = 0x00010000;
  private const int WS_SYSMENU = 0x00080000;
  private const int WS_EX_WINDOWEDGE = 0x00000100;

  // SetWindowPos flags
  private const uint SWP_FRAMECHANGED = 0x0020;
  private const uint SWP_NOMOVE = 0x0002;
  private const uint SWP_NOSIZE = 0x0001;
  private const uint SWP_SHOWWINDOW = 0x0040;
  private const uint SWP_NOACTIVATE = 0x0010;

  // MonitorFromWindow flags
  private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

  // ShowWindow commands
  private const int SW_MAXIMIZE = 3;
  private const int SW_RESTORE = 9;

  // WinEvent constants
  private const uint EVENT_OBJECT_CREATE = 0x8000;
  private const uint EVENT_OBJECT_DESTROY = 0x8001;
  private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
  private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
  private const int OBJID_WINDOW = 0;

  private static readonly nint HWND_TOPMOST = -1;
  private static readonly nint HWND_NOTOPMOST = -2;

  private readonly Dictionary<nint, WindowOriginalState> _originalStates = [];
  private readonly Dictionary<string, AvBitmap?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

  private delegate void WinEventDelegate(
    nint hWinEventHook, uint eventType, nint hwnd,
    int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

  private WinEventDelegate? _winEventDelegate;
  private nint _hookHandle;
  private nint _nameChangeHookHandle;
  private Action? _onWindowChanged;

  #region P/Invoke

  [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
  private static partial int GetWindowLong(nint hWnd, int nIndex);

  [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
  private static partial int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
    int x, int y, int cx, int cy, uint uFlags);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool ShowWindow(nint hWnd, int nCmdShow);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsZoomed(nint hWnd);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsIconic(nint hWnd);

  [LibraryImport("user32.dll")]
  private static partial nint SetWinEventHook(
    uint eventMin, uint eventMax, nint hmodWinEventProc,
    nint lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool UnhookWinEvent(nint hWinEventHook);

  [LibraryImport("user32.dll")]
  private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

  [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

  [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
  private static partial int GetWindowTextLength(nint hWnd);

  [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
  private static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

  #endregion

  public List<WindowInfo> GetAvailableWindows(IEnumerable<nint> excludeHandles)
  {
    var excluded = excludeHandles.ToHashSet();
    var currentPid = Environment.ProcessId;
    var windows = new List<WindowInfo>();

    foreach (var proc in Process.GetProcesses())
      try
      {
        if (proc.Id == currentPid)
          continue;

        if (string.IsNullOrEmpty(proc.MainWindowTitle) ||
            proc.MainWindowHandle == 0 ||
            excluded.Contains(proc.MainWindowHandle))
          continue;

        windows.Add(new WindowInfo(proc.MainWindowHandle, proc.ProcessName, proc.MainWindowTitle)
        {
          Icon = GetCachedIcon(proc)
        });
      }
      finally
      {
        proc.Dispose();
      }

    return windows;
  }

  public unsafe string GetCurrentTitle(nint handle)
  {
    var length = GetWindowTextLength(handle);
    if (length <= 0) return "";

    var buffer = stackalloc char[length + 1];
    var copied = GetWindowText(handle, buffer, length + 1);
    return copied > 0 ? new string(buffer, 0, copied) : "";
  }

  public void StartWatching(Action onWindowChanged)
  {
    _onWindowChanged = onWindowChanged;
    _winEventDelegate = OnWinEvent;
    var ptr = Marshal.GetFunctionPointerForDelegate(_winEventDelegate);
    _hookHandle = SetWinEventHook(
      EVENT_OBJECT_CREATE, EVENT_OBJECT_DESTROY,
      0, ptr, 0, 0, WINEVENT_OUTOFCONTEXT);
    _nameChangeHookHandle = SetWinEventHook(
      EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE,
      0, ptr, 0, 0, WINEVENT_OUTOFCONTEXT);
  }

  public void StopWatching()
  {
    if (_hookHandle != 0)
    {
      UnhookWinEvent(_hookHandle);
      _hookHandle = 0;
    }

    if (_nameChangeHookHandle != 0)
    {
      UnhookWinEvent(_nameChangeHookHandle);
      _nameChangeHookHandle = 0;
    }

    _winEventDelegate = null;
    _onWindowChanged = null;
  }

  private void OnWinEvent(
    nint hWinEventHook, uint eventType, nint hwnd,
    int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
  {
    if (idObject == OBJID_WINDOW && hwnd != 0)
      _onWindowChanged?.Invoke();
  }

  public void MakeBorderless(nint handle)
  {
    StoreOriginalState(handle);

    // 1. Get full monitor rect before any style changes
    var hMonitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
    var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
    GetMonitorInfo(hMonitor, ref mi);
    var mon = mi.rcMonitor;

    // 2. Strip frame styles
    var style = GetWindowLong(handle, GWL_STYLE);
    style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
    SetWindowLong(handle, GWL_STYLE, style);

    // 3. TOPMOST flash — position to exact monitor rect and jump above all shell overlays
    SetWindowPos(handle, HWND_TOPMOST,
      mon.Left, mon.Top, mon.Right - mon.Left, mon.Bottom - mon.Top,
      SWP_FRAMECHANGED | SWP_SHOWWINDOW);

    // 4. Drop to NOTOPMOST — lands at top of normal Z-order band
    SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0,
      SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
  }

  public void RestoreWindow(nint handle)
  {
    // Re-add standard window styles
    var style = GetWindowLong(handle, GWL_STYLE);
    var exStyle = GetWindowLong(handle, GWL_EXSTYLE);

    style |= WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
    exStyle |= WS_EX_WINDOWEDGE;

    SetWindowLong(handle, GWL_STYLE, style);
    SetWindowLong(handle, GWL_EXSTYLE, exStyle);

    SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0,
      SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

    if (_originalStates.Remove(handle, out var state))
    {
      if (state.WasMaximized)
      {
        ShowWindow(handle, SW_MAXIMIZE);
      }
      else
      {
        ShowWindow(handle, SW_RESTORE);

        if (!state.WasMinimized)
        {
          var width = state.OriginalRect.Right - state.OriginalRect.Left;
          var height = state.OriginalRect.Bottom - state.OriginalRect.Top;
          SetWindowPos(handle, 0, state.OriginalRect.Left, state.OriginalRect.Top,
            width, height, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
      }
    }
    else
    {
      ShowWindow(handle, SW_RESTORE);
    }
  }

  private void StoreOriginalState(nint handle)
  {
    if (_originalStates.ContainsKey(handle))
      return;

    GetWindowRect(handle, out var rect);
    _originalStates[handle] = new WindowOriginalState(rect, IsZoomed(handle), IsIconic(handle));
  }

  private AvBitmap? GetCachedIcon(Process proc)
  {
    try
    {
      var path = proc.MainModule?.FileName;
      if (path is null) return null;

      if (_iconCache.TryGetValue(path, out var cached))
        return cached;

      var icon = ExtractIconFromPath(path);
      _iconCache[path] = icon;
      return icon;
    }
    catch
    {
      return null; // Access denied for elevated / system processes
    }
  }

  private static AvBitmap? ExtractIconFromPath(string exePath)
  {
    using var icon = Icon.ExtractAssociatedIcon(exePath);
    if (icon is null) return null;

    using var bmp = icon.ToBitmap();
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    ms.Position = 0;
    return new AvBitmap(ms);
  }
}