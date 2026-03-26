using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BLauncher.Services;

public class LoaderVersion
{
    public string Version { get; set; } = "";
    public bool IsStable { get; set; } = true;
}

public class ModLoaderService
{
    private static readonly HttpClient Http = new();

    // ─── FABRIC ──────────────────────────────────────────────────────────────
    // Returns Fabric loader versions compatible with the given Minecraft version
    public async Task<List<LoaderVersion>> GetFabricLoadersAsync(string minecraftVersion)
    {
        try
        {
            // First verify that the MC version is supported by Fabric
            var mcUrl = "https://meta.fabricmc.net/v2/versions/game";
            var mcJson = await Http.GetStringAsync(mcUrl);
            var mcVersions = JsonSerializer.Deserialize<List<FabricGameVersion>>(mcJson);
            bool supported = mcVersions?.Exists(v => v.Version == minecraftVersion) ?? false;
            if (!supported) return new List<LoaderVersion> { new() { Version = "Not supported", IsStable = false } };

            // Fetch loader versions
            var url = "https://meta.fabricmc.net/v2/versions/loader";
            var json = await Http.GetStringAsync(url);
            var loaders = JsonSerializer.Deserialize<List<FabricLoaderVersion>>(json);

            var result = new List<LoaderVersion>();
            if (loaders == null) return result;
            foreach (var l in loaders)
            {
                result.Add(new LoaderVersion { Version = l.Version, IsStable = l.Stable });
                if (result.Count >= 20) break;
            }
            return result;
        }
        catch (Exception ex)
        {
            return new List<LoaderVersion> { new() { Version = $"Error: {ex.Message}", IsStable = false } };
        }
    }

    // ─── FORGE ───────────────────────────────────────────────────────────────
    // Returns Forge versions for the given Minecraft version using the Forge promotions API
    public async Task<List<LoaderVersion>> GetForgeVersionsAsync(string minecraftVersion)
    {
        try
        {
            MainWindow.AppendLog($"[DEBUG] Fetching Forge versions for MC: '{minecraftVersion}'");
            var url = $"https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
            var json = await Http.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ForgePromotions>(json);

            var result = new List<LoaderVersion>();
            if (data?.Promos == null) return result;

            string recKey = $"{minecraftVersion}-recommended";
            string latKey = $"{minecraftVersion}-latest";

            if (data.Promos.TryGetValue(recKey, out var rec)) {
                MainWindow.AppendLog($"[DEBUG] Found Forge Recommended: {rec}");
                result.Add(new LoaderVersion { Version = $"{minecraftVersion}-{rec} (Recommended)", IsStable = true });
            }

            if (data.Promos.TryGetValue(latKey, out var lat) && lat != (data.Promos.GetValueOrDefault(recKey) ?? "")) {
                MainWindow.AppendLog($"[DEBUG] Found Forge Latest: {lat}");
                result.Add(new LoaderVersion { Version = $"{minecraftVersion}-{lat} (Latest)", IsStable = false });
            }

            if (result.Count == 0)
                result.Add(new LoaderVersion { Version = "No Forge for this version", IsStable = false });

            return result;
        }
        catch (Exception ex)
        {
            MainWindow.AppendLog($"[DEBUG] Forge Error: {ex.Message}");
            return new List<LoaderVersion> { new() { Version = $"Error: {ex.Message}", IsStable = false } };
        }
    }

    // ─── NEOFORGE ────────────────────────────────────────────────────────────
    // Returns NeoForge versions from the NeoForge Maven metadata
    public async Task<List<LoaderVersion>> GetNeoForgeVersionsAsync(string minecraftVersion)
    {
        try
        {
            MainWindow.AppendLog($"[DEBUG] Fetching NeoForge versions for MC: '{minecraftVersion}'");
            var url = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";
            var json = await Http.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<NeoForgeVersionList>(json);

            if (data?.Versions == null || data.Versions.Count == 0)
                return new List<LoaderVersion> { new() { Version = "No NeoForge for this version", IsStable = false } };

            string prefix = minecraftVersion.StartsWith("1.") ? minecraftVersion[2..] : minecraftVersion;
            var parts = prefix.Split('.');
            
            var matching = new List<string>();
            foreach (var v in data.Versions)
            {
                var vParts = v.Split('.');
                if (vParts.Length >= 2 && vParts[0] == parts[0] && (parts.Length < 2 || vParts[1] == parts[1])) {
                    if (!v.Contains("-")) matching.Add(v);
                }
            }
            
            MainWindow.AppendLog($"[DEBUG] Found {matching.Count} matching NeoForge versions.");
            matching.Reverse();
            var result = new List<LoaderVersion>();
            for (int i = 0; i < Math.Min(matching.Count, 15); i++)
                result.Add(new LoaderVersion { Version = matching[i], IsStable = true });

            return result;
        }
        catch (Exception ex)
        {
            MainWindow.AppendLog($"[DEBUG] NeoForge Error: {ex.Message}");
            return new List<LoaderVersion> { new() { Version = $"Error: {ex.Message}", IsStable = false } };
        }
    }

    // ─── Internal JSON models ──────────────────────────────────────────────
    private class FabricGameVersion
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("stable")] public bool Stable { get; set; }
    }

    private class FabricLoaderVersion
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("stable")] public bool Stable { get; set; }
    }

    private class ForgePromotions
    {
        [JsonPropertyName("promos")] public Dictionary<string, string>? Promos { get; set; }
    }

    private class NeoForgeVersionList
    {
        [JsonPropertyName("versions")] public List<string>? Versions { get; set; }
    }
}
