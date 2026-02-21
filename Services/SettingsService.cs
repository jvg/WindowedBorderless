using System.Text.Json;
using WindowedBorderless.Models;

namespace WindowedBorderless.Services;

public static class SettingsService
{
  private static readonly string SettingsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "WindowedBorderless");

  private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

  public static Settings Load()
  {
    if (!File.Exists(SettingsFile))
      return new Settings();

    try
    {
      var json = File.ReadAllText(SettingsFile);
      return JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings) ?? new Settings();
    }
    catch
    {
      return new Settings();
    }
  }

  public static void Save(Settings settings)
  {
    Directory.CreateDirectory(SettingsDir);
    var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.Settings);
    File.WriteAllText(SettingsFile, json);
  }
}