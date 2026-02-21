namespace WindowedBorderless.Models;

public class RememberedWindow
{
  public string ProcessName { get; set; } = "";
  public string TitleHint { get; set; } = "";
}

public class Settings
{
  public List<RememberedWindow> RememberedWindows { get; set; } = [];
  public List<string> IgnoredDisplayNames { get; set; } = [];
  public bool RestoreOnClose { get; set; } = true;
  public string Theme { get; set; } = "System";
  public List<string> ForcedProcessNames { get; set; } = [];
}
