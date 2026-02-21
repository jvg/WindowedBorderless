using System.Text.Json;
using WindowedBorderless.Models;

namespace WindowedBorderless.Services;

public static class SettingsService
{
  private static readonly string SettingsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "WindowedBorderless");

  private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

  public static Settings Load()
  {
    if (!File.Exists(SettingsFile))
      return new Settings();

    try
    {
      var json = File.ReadAllText(SettingsFile);
      return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
    }
    catch
    {
      return new Settings();
    }
  }

  public static void Save(Settings settings)
  {
    Directory.CreateDirectory(SettingsDir);
    var json = JsonSerializer.Serialize(settings, JsonOptions);
    File.WriteAllText(SettingsFile, json);
  }
}