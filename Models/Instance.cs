using System;

namespace BLauncher.Models;

public class InstanceMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Instance";
    public string Version { get; set; } = "1.21.1";
    public string ModLoader { get; set; } = "Vanilla"; // Vanilla, Fabric, Forge, NeoForge
    public string LoaderVersion { get; set; } = "";    // Specific loader version (empty = latest/vanilla)
    public string Icon { get; set; } = "ms-appx:///Assets/DefaultIcon.png";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastPlayedAt { get; set; }
}
