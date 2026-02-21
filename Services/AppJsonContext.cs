using System.Text.Json.Serialization;
using WindowedBorderless.Filtering;
using WindowedBorderless.Models;

namespace WindowedBorderless.Services;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(List<IgnoreListEntry>))]
internal partial class AppJsonContext : JsonSerializerContext;
