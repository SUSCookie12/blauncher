using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BLauncher.Models;

public class VersionManifest
{
    [JsonPropertyName("latest")] public LatestVersions Latest { get; set; } = new();
    [JsonPropertyName("versions")] public List<VersionInfo> Versions { get; set; } = new();
}

public class LatestVersions
{
    [JsonPropertyName("release")] public string Release { get; set; } = "";
    [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = "";
}

public class VersionInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}
