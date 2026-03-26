using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BLauncher.Models;

namespace BLauncher.Services;

public class VersionService
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    private static readonly HttpClient Http = new();

    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
            
            var list = new List<string>();
            if (manifest == null) return list;

            // Priority logic: Latest Release first, then other releases
            list.Add($"{manifest.Latest.Release} (Latest)");

            foreach (var v in manifest.Versions)
            {
                if (v.Type == "release" && v.Id != manifest.Latest.Release)
                {
                    list.Add(v.Id);
                }
                
                // Limit the list to make it clean
                if (list.Count >= 20) break;
            }

            return list;
        }
        catch { return new List<string> { "1.21.1 (Fallback)", "1.20.1", "1.12.2" }; }
    }
}
