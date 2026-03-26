using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BLauncher.Models;

public class ModInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Unknown Mod";
    public string Version { get; set; } = "0.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Filename { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Website { get; set; }
}

public class ModService
{
    public List<ModInfo> LoadMods(string modsPath)
    {
        var list = new List<ModInfo>();
        if (!Directory.Exists(modsPath)) return list;

        foreach (var file in Directory.GetFiles(modsPath, "*.jar*"))
        {
            var filename = Path.GetFileName(file);
            bool isEnabled = !filename.EndsWith(".disabled");
            
            var info = ExtractMetadata(file);
            info.Filename = filename;
            info.Enabled = isEnabled;
            if (string.IsNullOrEmpty(info.Name)) info.Name = filename.Replace(".jar", "").Replace(".disabled", "");
            
            list.Add(info);
        }
        return list;
    }

    private ModInfo ExtractMetadata(string jarPath)
    {
        var info = new ModInfo();
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            
            // Try Fabric
            var fabricEntry = zip.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var s = fabricEntry.Open();
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n)) info.Name = n.GetString() ?? "";
                if (root.TryGetProperty("version", out var v)) info.Version = v.GetString() ?? "";
                if (root.TryGetProperty("authors", out var a)) {
                    if (a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0)
                        info.Author = a[0].ValueKind == JsonValueKind.String ? a[0].GetString() ?? "" : a[0].TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                }
                if (root.TryGetProperty("description", out var d)) info.Description = d.GetString() ?? "";
                if (root.TryGetProperty("contact", out var c) && c.TryGetProperty("homepage", out var h)) info.Website = h.GetString();
                return info;
            }

            // Try Forge/NeoForge (mods.toml)
            var tomlEntry = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
            if (tomlEntry != null)
            {
                using (var reader = new StreamReader(tomlEntry.Open()))
                {
                    string content = reader.ReadToEnd();
                    // VERY basic TOML parser for name/version
                    info.Name = GetValue(content, "displayName");
                    info.Version = GetValue(content, "version");
                    info.Author = GetValue(content, "authors");
                    info.Description = GetValue(content, "description");
                }
            }
        }
        catch { }
        return info;
    }

    private string GetValue(string toml, string key)
    {
        var line = toml.Split('\n').FirstOrDefault(l => l.Trim().StartsWith(key + "="));
        if (line == null) return "";
        return line.Split('=')[1].Trim().Trim('"').Trim('\'');
    }

    public void ToggleMod(string modsPath, ModInfo mod, bool state)
    {
        string oldPath = Path.Combine(modsPath, mod.Filename);
        string newFilename = state ? mod.Filename.Replace(".disabled", "") : mod.Filename + ".disabled";
        string newPath = Path.Combine(modsPath, newFilename);

        if (File.Exists(oldPath))
        {
            File.Move(oldPath, newPath);
            mod.Filename = newFilename;
            mod.Enabled = state;
        }
    }
}
