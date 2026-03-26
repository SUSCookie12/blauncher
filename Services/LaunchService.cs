using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BLauncher.Models;
using System.Linq;
using System.IO.Compression;

namespace BLauncher.Services;

public class LaunchService
{
    private static readonly HttpClient Http = new();
    public Process? CurrentProcess { get; private set; }
    public bool IsRunning => CurrentProcess != null && !CurrentProcess.HasExited;

    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BLauncher");

    private static readonly string MinecraftDir = Path.Combine(BaseDir, "minecraft");

    public async Task LaunchAsync(InstanceMetadata instance, string username, Action<string> onLog)
    {
        try { await LaunchInternalAsync(instance, username, onLog); }
        catch (Exception ex) {
            onLog($"[FATAL] Launcher Error: {ex.Message}");
            MainWindow.AppendLog($"Stack: {ex.StackTrace}");
        }
    }

    private async Task LaunchInternalAsync(InstanceMetadata instance, string username, Action<string> onLog)
    {
        onLog($"Starting launch project: {instance.Name}");
        
        string instanceDir = Path.Combine(BaseDir, "instances", instance.Id);
        string versionsDir  = Path.Combine(MinecraftDir, "versions");
        string librariesDir = Path.Combine(MinecraftDir, "libraries");
        string assetsDir    = Path.Combine(MinecraftDir, "assets");
        string nativesDir   = Path.Combine(MinecraftDir, "natives", instance.Version);

        Directory.CreateDirectory(instanceDir);
        Directory.CreateDirectory(versionsDir);
        Directory.CreateDirectory(librariesDir);
        Directory.CreateDirectory(assetsDir);
        
        // Clean and recreate natives dir to prevent architecture contamination
        if (Directory.Exists(nativesDir)) Directory.Delete(nativesDir, true);
        Directory.CreateDirectory(nativesDir);

        var versionMeta = await GetVersionMetaAsync(instance.Version, versionsDir, onLog);
        if (versionMeta == null) return;

        string clientJar = Path.Combine(versionsDir, instance.Version, $"{instance.Version}.jar");
        if (!File.Exists(clientJar)) {
            Directory.CreateDirectory(Path.GetDirectoryName(clientJar)!);
            await DownloadFileAsync(versionMeta.Downloads?.Client?.Url ?? "", clientJar, onLog);
        }

        var classpath = new List<string>();
        foreach (var lib in versionMeta.Libraries ?? new()) {
            if (!IsAllowed(lib.Rules)) continue;

            bool isNative = false;
            McArtifact? nativeArt = null;

            // 1. Process Main Artifact
            if (lib.Downloads?.Artifact != null) {
                string libPath = Path.Combine(librariesDir, lib.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(libPath)) {
                    Directory.CreateDirectory(Path.GetDirectoryName(libPath)!);
                    await DownloadFileAsync(lib.Downloads.Artifact.Url, libPath, onLog);
                }
                classpath.Add(libPath);
                
                if (lib.Downloads.Artifact.Path.Contains("natives-windows")) {
                    isNative = true;
                    nativeArt = lib.Downloads.Artifact;
                }
            }

            // 2. Process Classifiers (Legacy style)
            if (lib.Downloads?.Classifiers != null) {
                var cls = lib.Downloads.Classifiers;
                McArtifact? best = null;
                // Priority: 64-bit -> Windows Generic -> Any Windows (fuzzy)
                if (cls.TryGetValue("natives-windows-x86_64", out var a64)) best = a64;
                else if (cls.TryGetValue("natives-windows-64", out var a64_2)) best = a64_2;
                else if (cls.TryGetValue("natives-windows", out var aWin)) best = aWin;
                else best = cls.Values.OrderByDescending(v => v.Path.Contains("64") || v.Path.Contains("x64")).FirstOrDefault(v => v.Path.Contains("natives-windows"));

                if (best != null) {
                    isNative = true;
                    nativeArt = best;
                }
            }

            if (isNative && nativeArt != null) {
                string nJar = Path.Combine(librariesDir, nativeArt.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(nJar)) {
                    Directory.CreateDirectory(Path.GetDirectoryName(nJar)!);
                    await DownloadFileAsync(nativeArt.Url, nJar, onLog);
                }
                
                try {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(nJar);
                    foreach (var entry in zip.Entries) {
                        if (entry.Name.EndsWith(".dll")) {
                            string destP = Path.Combine(nativesDir, entry.Name);
                            onLog($"  Deploying: {entry.Name}");
                            entry.ExtractToFile(destP, true);
                        }
                    }
                } catch(Exception e) { onLog("Native extraction failed: " + e.Message); }
            }
        }
        classpath.Add(clientJar);

        string mainClass = versionMeta.MainClass ?? "net.minecraft.client.main.Main";
        var jvmArgs = new List<string> { 
            "-Xmx4G", 
            "-Xms512M", 
            $"-Djava.library.path={nativesDir}",
            $"-Dorg.lwjgl.librarypath={nativesDir}"
        };

        if (instance.ModLoader == "Fabric" && !string.IsNullOrEmpty(instance.LoaderVersion)) {
            var (ok, fabricMain) = await SetupFabricAsync(instance.Version, instance.LoaderVersion, librariesDir, classpath, onLog);
            if (ok) {
                mainClass = fabricMain;
                jvmArgs.Add($"-Dfabric.gameJarPath={clientJar}");
            }
        }
        else if ((instance.ModLoader == "Forge" || instance.ModLoader == "NeoForge") && !string.IsNullOrEmpty(instance.LoaderVersion)) {
            var (ok, loaderMain) = await SetupForgeLikeAsync(instance, librariesDir, classpath, onLog);
            if (ok) {
                mainClass = loaderMain;
                // MODULAR RUNTIME FLAGS (Required for MC 1.17+ / Forge 1.20+)
                jvmArgs.Add("-Dforgelog.level=debug");
                jvmArgs.Add($"-DignoreList=bootstraplauncher,forge,neoforge,{instance.Version}.jar");
                jvmArgs.Add("-DmergeModules=jboss-marshalling-river-2.0.12.Final.jar,jboss-marshalling-2.0.12.Final.jar");
                
                // Essential Java Module openings
                jvmArgs.Add("--add-opens=java.base/java.util.jar=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.lang.invoke=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.lang.reflect=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.io=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.util=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.util.concurrent=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/sun.nio.ch=ALL-UNNAMED");
                jvmArgs.Add("--add-opens=java.base/java.nio.file=ALL-UNNAMED");
            }
        }

        string assetIndex = versionMeta.AssetIndex?.Id ?? instance.Version;
        string assetIndexPath = Path.Combine(assetsDir, "indexes", $"{assetIndex}.json");
        if (!File.Exists(assetIndexPath)) {
            Directory.CreateDirectory(Path.GetDirectoryName(assetIndexPath)!);
            await DownloadFileAsync(versionMeta.AssetIndex?.Url ?? "", assetIndexPath, onLog);
        }
        await DownloadAssetObjectsAsync(assetIndexPath, Path.Combine(assetsDir, "objects"), onLog);

        string? javaPath = await FindJavaAsync(versionMeta.JavaVersion?.MajorVersion ?? 17, onLog);
        if (javaPath == null) { onLog($"ERROR: Java {versionMeta.JavaVersion?.MajorVersion ?? 17} not found."); return; }

        var finalCP = classpath.Distinct().ToList(); // Simplified for stability, rules will handle the rest
        string argsFile = Path.Combine(instanceDir, "launch_args.txt");
        onLog($"Finalizing launch command ({finalCP.Count} libs)...");
        
        var argLines = new List<string>();
        foreach (var arg in jvmArgs) argLines.Add(arg);
        argLines.Add("-cp");
        argLines.Add(string.Join(Path.PathSeparator, finalCP));
        argLines.Add(mainClass);

        argLines.Add("--username"); argLines.Add(username);
        argLines.Add("--version");  argLines.Add(instance.Version);
        argLines.Add("--gameDir");  argLines.Add(instanceDir);
        argLines.Add("--assetsDir");argLines.Add(assetsDir);
        argLines.Add("--assetIndex");argLines.Add(assetIndex);
        argLines.Add("--uuid");     argLines.Add(Guid.NewGuid().ToString("N"));
        argLines.Add("--accessToken"); argLines.Add("0");
        argLines.Add("--userType"); argLines.Add("offline");

        // Write the args file (one arg per line or quoted)
        var finalArgs = argLines.Select(a => a.Contains(" ") ? $"\"{a}\"" : a).ToList();
        await File.WriteAllLinesAsync(argsFile, finalArgs);
        onLog($"Finalizing launch command ({finalCP.Count} libs) via @launch_args.txt...");
        onLog($"--- Launch Arguments ---");
        foreach(var arg in argLines.Take(10)) onLog(arg);
        onLog("...");
        onLog($"--- End Arguments ---");
        
        var psi = new ProcessStartInfo {
            FileName = javaPath.Trim('"'),
            Arguments = $"@\"{argsFile}\"",
            WorkingDirectory = instanceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // Inject natives into PATH for manual DLL loading
        string? existingPath = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH");
        psi.EnvironmentVariables["PATH"] = $"{nativesDir};{existingPath}";
        onLog($"Native library path: {nativesDir}");

        try {
            CurrentProcess = Process.Start(psi);
            if (CurrentProcess == null) { onLog("ERROR: Process failed to start."); return; }
        } catch (Exception ex) {
            onLog($"[CRITICAL] Launch failed: {ex.Message}");
            return;
        }

        var proc = CurrentProcess;
        _ = Task.Run(async () => { try { string? l; while((l = await proc.StandardOutput.ReadLineAsync()) != null) MainWindow.AppendLog("[MC] " + l); } catch{} });
        _ = Task.Run(async () => { try { string? l; while((l = await proc.StandardError.ReadLineAsync()) != null) MainWindow.AppendLog("[MC ERR] " + l); } catch{} });
        _ = Task.Run(async () => { try { await proc.WaitForExitAsync(); int c = proc.ExitCode; CurrentProcess = null; MainWindow.AppendLog($"Process finished with code {c}"); } catch{} });
    }

    private async Task<(bool, string)> SetupFabricAsync(string mc, string loader, string libDir, List<string> cp, Action<string> onLog)
    {
        try {
            onLog($"Injecting Fabric {loader} dependencies...");
            string url = $"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json";
            var json = await Http.GetStringAsync(url);
            var meta = JsonSerializer.Deserialize<FabricProfileMeta>(json);
            if (meta == null) return (false, "");

            var fLibs = new List<string>();
            foreach (var l in meta.Libraries ?? new()) {
                string p = MavenToPath(libDir, l.Name ?? "");
                if (!File.Exists(p)) await DownloadFileAsync(l.Url != null ? MavenToUrl(l.Url, l.Name!) : MavenToUrl("https://maven.fabricmc.net/", l.Name!), p, onLog);
                fLibs.Add(p);
            }
            cp.AddRange(fLibs);
            return (true, meta.MainClass ?? "net.fabricmc.loader.impl.launch.knot.KnotClient");
        } catch { return (false, ""); }
    }

    private async Task<(bool, string)> SetupForgeLikeAsync(InstanceMetadata inst, string libDir, List<string> cp, Action<string> onLog)
    {
        try {
            onLog($"Detecting {inst.ModLoader} binaries...");
            string mavenBase = inst.ModLoader == "Forge" ? "https://maven.minecraftforge.net/" : "https://maven.neoforged.net/releases/";
            string group = inst.ModLoader == "Forge" ? "net.minecraftforge:forge" : "net.neoforged:neoforge";
            
            string ver = inst.LoaderVersion.Split(' ')[0];
            string coord = $"{group}:{ver}";
            string path = MavenToPath(libDir, coord);

            if (!File.Exists(path)) {
                string[] classifiers = { "universal", "shim", "", "installer" };
                bool ok = false;
                foreach(var cf in classifiers) {
                    string fullCoord = string.IsNullOrEmpty(cf) ? coord : $"{coord}:{cf}";
                    string url = MavenToUrl(mavenBase, fullCoord);
                    try {
                        var res = await Http.GetAsync(url);
                        if (res.IsSuccessStatusCode) {
                            var data = await res.Content.ReadAsByteArrayAsync();
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            await File.WriteAllBytesAsync(path, data);
                            onLog($"✓ Found {inst.ModLoader} binary: {(string.IsNullOrEmpty(cf) ? "main" : cf)}");
                            ok = true; break;
                        }
                    } catch {}
                }
                if (!ok) { onLog("ERROR: Game binaries not found."); return (false, ""); }
            }
            
            cp.Insert(0, path);

            string detectedMain = inst.ModLoader == "Forge" ? "cpw.mods.bootstraplauncher.BootstrapLauncher" : "net.neoforged.neoforge.bootstrap.NeoForgeBootstrap";
            try {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                               // 1. Try both version.json and install_profile.json for complete deps
                var depsMetadata = new List<string> { "version.json", "extra/version.json", "install_profile.json" };
                foreach(var entryName in depsMetadata) {
                    var entry = zip.GetEntry(entryName);
                    if (entry == null) continue;
                    
                    onLog($"  Scanning {entryName} for dependencies...");
                    using var sr = new StreamReader(entry.Open());
                    var rawJson = await sr.ReadToEndAsync();
                    
                    // UNIVERSAL JSON SCANNER: Look for any "libraries" array regardless of nesting
                    using var doc = JsonDocument.Parse(rawJson);
                    var libArrays = new List<JsonElement>();
                    FindLibraryArrays(doc.RootElement, libArrays);

                    foreach(var arr in libArrays) {
                        foreach(var lObj in arr.EnumerateArray()) {
                            var l = JsonSerializer.Deserialize<McLibrary>(lObj.GetRawText());
                            if (l == null) continue;
                            
                            var downloads = new List<McArtifact>();
                            if (l.Downloads?.Artifact != null) downloads.Add(l.Downloads.Artifact);
                            if (l.Downloads?.Classifiers != null) {
                                foreach(var clVal in l.Downloads.Classifiers.Values) {
                                    if(clVal != null) downloads.Add(clVal);
                                }
                            }
                            
                            if (downloads.Count == 0 && !string.IsNullOrEmpty(l.Name)) {
                                string lp = MavenToPath(libDir, l.Name);
                                if (!File.Exists(lp)) {
                                    bool ok = false;
                                    var mavens = new[] { "https://maven.minecraftforge.net/", "https://repo1.maven.org/maven2/", "https://maven.neoforged.net/releases/" };
                                    foreach(var m in mavens) {
                                        try {
                                            var res = await Http.GetAsync(MavenToUrl(m, l.Name));
                                            if (res.IsSuccessStatusCode) {
                                                Directory.CreateDirectory(Path.GetDirectoryName(lp)!);
                                                await File.WriteAllBytesAsync(lp, await res.Content.ReadAsByteArrayAsync());
                                                ok = true; break;
                                            }
                                        } catch {}
                                    }
                                    if (!ok) onLog($"! Warning: Library {l.Name} not found on any Maven.");
                                }
                                cp.Insert(0, lp);
                            }

                            foreach(var dl in downloads) {
                                if (string.IsNullOrEmpty(dl.Path) || string.IsNullOrEmpty(dl.Url)) continue;
                                string lp = Path.Combine(libDir, dl.Path.Replace('/', Path.DirectorySeparatorChar));
                                if (!File.Exists(lp)) {
                                    Directory.CreateDirectory(Path.GetDirectoryName(lp)!);
                                    await DownloadFileAsync(dl.Url, lp, onLog);
                                }
                                cp.Insert(0, lp);
                            }
                        }
                    }
                    // Attempt to grab mainClass if present
                    if (doc.RootElement.TryGetProperty("mainClass", out var mcProp)) detectedMain = mcProp.GetString() ?? detectedMain;
                }

                var manifestEntry = zip.GetEntry("META-INF/MANIFEST.MF");
                if (manifestEntry != null) {
                    using var sr = new StreamReader(manifestEntry.Open());
                    string? line;
                    while((line = sr.ReadLine()) != null) {
                        if (line.StartsWith("Main-Class: ")) {
                            string mc = line.Substring("Main-Class: ".Length).Trim();
                            if (!mc.Contains("installer")) detectedMain = mc;
                            break;
                        }
                    }
                }
            } catch (Exception ex) { onLog("! Warning: Loader zip parsing failed: " + ex.Message); }
            onLog($"✓ Loader setup complete ({cp.Count} libs)");
            return (true, detectedMain);
        } catch { return (false, ""); }
    }

    private void FindLibraryArrays(JsonElement element, List<JsonElement> found)
    {
        if (element.ValueKind == JsonValueKind.Object) {
            foreach(var prop in element.EnumerateObject()) {
                if (prop.Name == "libraries" && prop.Value.ValueKind == JsonValueKind.Array) found.Add(prop.Value);
                else FindLibraryArrays(prop.Value, found);
            }
        } else if (element.ValueKind == JsonValueKind.Array) {
            foreach(var item in element.EnumerateArray()) FindLibraryArrays(item, found);
        }
    }

    private async Task<McVersionMeta?> GetVersionMetaAsync(string version, string dir, Action<string> onLog)
    {
        string path = Path.Combine(dir, $"{version}.json");
        if (!File.Exists(path)) {
            var res = await Http.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest.json");
            var manifest = JsonSerializer.Deserialize<McManifest>(res);
            var v = manifest?.Versions?.Find(x => x.Id == version);
            if (v == null) return null;
            await DownloadFileAsync(v.Url, path, onLog);
        }
        return JsonSerializer.Deserialize<McVersionMeta>(File.ReadAllText(path));
    }

    private async Task DownloadAssetObjectsAsync(string index, string dir, Action<string> onLog)
    {
        var meta = JsonSerializer.Deserialize<McAssetObjects>(File.ReadAllText(index));
        if (meta?.Objects == null) return;
        var missing = meta.Objects.Values.Select(o => (o.Hash, Path.Combine(dir, o.Hash[..2], o.Hash))).Where(x => !File.Exists(x.Item2)).ToList();
        if (missing.Count == 0) return;
        onLog($"Syncing {missing.Count} game assets...");
        foreach (var batch in missing.Chunk(30)) {
            await Task.WhenAll(batch.Select(async x => {
                for(int i=0; i<3; i++) {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(x.Item2)!);
                    var b = await Http.GetByteArrayAsync($"https://resources.download.minecraft.net/{x.Hash[..2]}/{x.Hash}");
                    await File.WriteAllBytesAsync(x.Item2, b); break; } catch { await Task.Delay(500); }
                }
            }));
        }
    }

    private async Task DownloadFileAsync(string url, string dest, Action<string> onLog) { try { var b = await Http.GetByteArrayAsync(url); await File.WriteAllBytesAsync(dest, b); } catch(Exception e) { onLog("Download failure: " + e.Message); } }

    private string MavenToPath(string dir, string coord) {
        var p = coord.Split(':');
        string g = p[0].Replace('.','\\');
        string a = p[1];
        string v = p[2];
        string c = p.Length > 3 ? "-" + p[3] : "";
        return Path.Combine(dir, g, a, v, $"{a}-{v}{c}.jar");
    }
    private string MavenToUrl(string baseU, string coord) {
        var p = coord.Split(':');
        string g = p[0].Replace('.','/');
        string a = p[1];
        string v = p[2];
        string c = p.Length > 3 ? "-" + p[3] : "";
        return $"{baseU}{g}/{a}/{v}/{a}-{v}{c}.jar";
    }

    private async Task<string?> FindJavaAsync(int ver, Action<string> onLog) {
        string cacheDir = Path.Combine(BaseDir, "runtimes", $"jre-{ver}");
        string cachedExe = Path.Combine(cacheDir, "bin", "java.exe");
        if (File.Exists(cachedExe)) return cachedExe;
        if (Directory.Exists(cacheDir)) {
            foreach(var d in Directory.GetDirectories(cacheDir)) {
                string nestedExe = Path.Combine(d, "bin", "java.exe");
                if (File.Exists(nestedExe)) return nestedExe;
            }
        }

        string? home = Environment.GetEnvironmentVariable("JAVA_HOME");
        var paths = new List<string>();
        if (home != null) paths.Add(home);
        var roots = new[] { @"C:\Program Files\Java", @"C:\Program Files\Microsoft", @"C:\Program Files\Eclipse Adoptium" };
        foreach(var r in roots) {
            if(!Directory.Exists(r)) continue;
            paths.AddRange(Directory.GetDirectories(r).OrderByDescending(x => x));
        }

        foreach(var p in paths) {
            if (p.Contains(ver.ToString()) && File.Exists(Path.Combine(p, "bin", "java.exe"))) {
                return Path.Combine(p, "bin", "java.exe");
            }
        }

        onLog($"Java {ver} not found locally! Downloading JRE {ver}...");
        try {
            Directory.CreateDirectory(cacheDir);
            string zipPath = Path.Combine(cacheDir, "jre.zip");
            string url = $"https://api.adoptium.net/v3/binary/latest/{ver}/ga/windows/x64/jre/hotspot/normal/eclipse";
            
            await DownloadFileAsync(url, zipPath, onLog);
            onLog($"Extracting Java {ver}...");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, cacheDir, true);
            File.Delete(zipPath);
            
            foreach(var d in Directory.GetDirectories(cacheDir)) {
                string nestedExe = Path.Combine(d, "bin", "java.exe");
                if (File.Exists(nestedExe)) return nestedExe;
            }
            if (File.Exists(cachedExe)) return cachedExe;
        } catch (Exception ex) {
            onLog($"Failed to download Java {ver}: {ex.Message}");
        }

        foreach(var p in paths) {
            if (File.Exists(Path.Combine(p, "bin", "java.exe"))) return Path.Combine(p, "bin", "java.exe");
        }
        return "java.exe";
    }

    public void Terminate() { try { CurrentProcess?.Kill(true); } catch{} }

    private class McManifest { [JsonPropertyName("versions")] public List<McManifestVersion>? Versions { get; set; } }
    private class McManifestVersion { [JsonPropertyName("id")] public string Id { get; set; } = ""; [JsonPropertyName("url")] public string Url { get; set; } = ""; }
    private class McVersionMeta { [JsonPropertyName("mainClass")] public string? MainClass { get; set; } [JsonPropertyName("libraries")] public List<McLibrary>? Libraries { get; set; } [JsonPropertyName("downloads")] public McDownloads? Downloads { get; set; } [JsonPropertyName("assetIndex")] public McAssetIndex? AssetIndex { get; set; } [JsonPropertyName("javaVersion")] public McJavaVersion? JavaVersion { get; set; } }
    private class McDownloads { [JsonPropertyName("client")] public McArtifact? Client { get; set; } }
    private class McLibrary { 
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("downloads")] public McLibraryDownloads? Downloads { get; set; } 
        [JsonPropertyName("rules")] public List<McRule>? Rules { get; set; }
    }
    private class McLibraryDownloads { 
        [JsonPropertyName("artifact")] public McArtifact? Artifact { get; set; }
        [JsonPropertyName("classifiers")] public Dictionary<string, McArtifact>? Classifiers { get; set; }
    }
    private class McArtifact { [JsonPropertyName("url")] public string Url { get; set; } = ""; [JsonPropertyName("path")] public string Path { get; set; } = ""; }
    private class McAssetIndex { [JsonPropertyName("id")] public string? Id { get; set; } [JsonPropertyName("url")] public string? Url { get; set; } }
    private class McJavaVersion { [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; } = 17; }
    private class FabricProfileMeta { [JsonPropertyName("mainClass")] public string? MainClass { get; set; } [JsonPropertyName("libraries")] public List<FabricLibrary>? Libraries { get; set; } }
    private class FabricLibrary { [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("url")] public string? Url { get; set; } }
    private class McAssetObjects { [JsonPropertyName("objects")] public Dictionary<string, McAssetObject>? Objects { get; set; } }
    private class McAssetObject { [JsonPropertyName("hash")] public string Hash { get; set; } = ""; }

    private bool IsAllowed(List<McRule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;
        bool allowed = false;
        foreach (var rule in rules) {
            if (rule.Action == "allow") {
                if (rule.Os == null || rule.Os.Name == "windows") allowed = true;
            } else if (rule.Action == "disallow") {
                if (rule.Os != null && rule.Os.Name == "windows") allowed = false;
            }
        }
        return allowed;
    }

    private class McRule { [JsonPropertyName("action")] public string Action { get; set; } = ""; [JsonPropertyName("os")] public McOsRule? Os { get; set; } }
    private class McOsRule { [JsonPropertyName("name")] public string? Name { get; set; } }
}
