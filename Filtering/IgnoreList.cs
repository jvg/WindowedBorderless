using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace WindowedBorderless.Filtering;

public record IgnoreListEntry(string ProcessName, string Description);

public record CategorizedEntry(string Category, string ProcessName, string Description);

public class IgnoreList
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private readonly HashSet<string> _ignored;

  public IReadOnlyList<CategorizedEntry> Entries { get; }

  private IgnoreList(List<CategorizedEntry> entries)
  {
    Entries = entries;
    _ignored = new HashSet<string>(
      entries.Select(e => e.ProcessName),
      StringComparer.OrdinalIgnoreCase);
  }

  public bool IsIgnored(string processName)
  {
    return _ignored.Contains(processName);
  }

  public bool IsIgnored(string processName, IReadOnlySet<string> overrides)
  {
    return _ignored.Contains(processName) && !overrides.Contains(processName);
  }

  public static string GetInfoUrl(string processName)
  {
    return $"https://www.file.net/process/{processName}.exe.html";
  }

  public static IgnoreList LoadDefault()
  {
    var assembly = Assembly.GetExecutingAssembly();
    var entries = new List<CategorizedEntry>();

    foreach (var resourceName in assembly.GetManifestResourceNames()
               .Where(n => n.Contains(".Filtering.Lists.") &&
                           n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
    {
      var category = ParseCategory(resourceName);

      using var stream = assembly.GetManifestResourceStream(resourceName)!;
      var batch = JsonSerializer.Deserialize<List<IgnoreListEntry>>(stream, JsonOptions) ?? [];
      entries.AddRange(batch.Select(e => new CategorizedEntry(category, e.ProcessName, e.Description)));
    }

    return new IgnoreList(entries);
  }

  private static string ParseCategory(string resourceName)
  {
    // Resource name like "WindowedBorderless.Filtering.Lists.system.json" → "System"
    var fileName = resourceName.Split('.')[^2]; // get the part before ".json"
    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(fileName);
  }
}
