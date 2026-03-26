using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BLauncher.Models;

namespace BLauncher.Services;

public class InstanceService
{
    private static readonly string InstancesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BLauncher", "instances");
    private static readonly string ManifestFile = Path.Combine(InstancesDir, "manifest.json");

    public string GetInstancePath(string id) => Path.Combine(InstancesDir, id);
    public string GetModsPath(string id) => Path.Combine(GetInstancePath(id), "mods");
    public string GetSavesPath(string id) => Path.Combine(GetInstancePath(id), "saves");
    public string GetServersFilePath(string id) => Path.Combine(GetInstancePath(id), "servers.dat");

    public InstanceService()
    {
        Directory.CreateDirectory(InstancesDir);
    }

    public List<InstanceMetadata> LoadInstances()
    {
        try
        {
            if (!File.Exists(ManifestFile)) return new List<InstanceMetadata>();
            var json = File.ReadAllText(ManifestFile);
            return JsonSerializer.Deserialize<List<InstanceMetadata>>(json) ?? new List<InstanceMetadata>();
        }
        catch { return new List<InstanceMetadata>(); }
    }

    public void SaveInstances(List<InstanceMetadata> instances)
    {
        try
        {
            var json = JsonSerializer.Serialize(instances, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ManifestFile, json);
        }
        catch { }
    }

    public void AddInstance(InstanceMetadata instance)
    {
        var all = LoadInstances();
        all.Add(instance);
        SaveInstances(all);
        
        Directory.CreateDirectory(Path.Combine(InstancesDir, instance.Id));
    }

    public void DeleteInstance(string id)
    {
        var all = LoadInstances();
        var target = all.Find(i => i.Id == id);
        if (target != null) {
            all.Remove(target);
            SaveInstances(all);
            
            // Cleanup folders (safely)
            try {
                string path = Path.Combine(InstancesDir, id);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            } catch {}
        }
    }

    public void UpdateInstance(InstanceMetadata instance)
    {
        var all = LoadInstances();
        var idx = all.FindIndex(i => i.Id == instance.Id);
        if (idx != -1) {
            all[idx] = instance;
            SaveInstances(all);
        }
    }
}
