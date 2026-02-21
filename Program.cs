using Avalonia;
using Velopack;

namespace WindowedBorderless;

internal static class Program
{
  [STAThread]
  public static void Main(string[] args)
  {
    VelopackApp.Build().Run();
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
  }

  public static AppBuilder BuildAvaloniaApp()
  {
    return AppBuilder.Configure<App>()
      .UsePlatformDetect() 
      .LogToTrace();
  }
}