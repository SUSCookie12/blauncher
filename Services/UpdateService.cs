using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Diagnostics;

namespace BLauncher.Services;

public class UpdateService
{
    public const string CurrentVersion = "26.0.1"; // Local version (First production 2026)
    private const string ApiKey = "AIzaSyAxkQA5tBqp_5fco6Y_8i2mhI15ECJeNh0";
    private const string ProjectId = "cookiemovie-27669";
    private const string ConfigUrl = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/services/blauncher?key={ApiKey}";
    private static readonly HttpClient Http = new();

    public async Task<(bool available, string? newerVersion, string? downloadUrl)> CheckForUpdateAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ConfigUrl);
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) 
            {
                MainWindow.AppendLog($"[Update] API failed: {response.StatusCode}");
                return (false, null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JObject.Parse(json);
            var fields = doc["fields"] as JObject;
            if (fields == null) return (false, null, null);

            string? remoteVer = fields["version"]?["stringValue"]?.ToString();
            string? downloadUrl = fields["downloadUrl"]?["stringValue"]?.ToString();

            if (string.IsNullOrEmpty(remoteVer) || string.IsNullOrEmpty(downloadUrl)) 
                return (false, null, null);

            MainWindow.AppendLog($"[Update] Checking version: {CurrentVersion} vs {remoteVer}");
            bool isNewer = IsNewer(remoteVer, CurrentVersion);
            return (isNewer, remoteVer, downloadUrl);
        }
        catch (Exception ex)
        {
            MainWindow.AppendLog($"[Update] Error: {ex.Message}");
            return (false, null, null);
        }
    }

    private bool IsNewer(string remote, string local)
    {
        try
        {
            string cleanRemote = remote.Trim().TrimStart('v', 'V');
            string cleanLocal = local.Trim().TrimStart('v', 'V');
            var vRemote = new Version(cleanRemote);
            var vLocal = new Version(cleanLocal);
            return vRemote > vLocal;
        }
        catch { return false; }
    }

    public async Task DownloadAndInstallAsync(string downloadUrl, Action<double> onProgress)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "BLauncher_Update.exe");
        
        using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var readBytes = 0L;
            
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    readBytes += bytesRead;
                    if (totalBytes > 0)
                        onProgress((double)readBytes / totalBytes * 100);
                }
            }
        }

        // Silent install flags (common for many installers: /SILENT or /S or /VERYSILENT)
        // We run it and exit
        Process.Start(new ProcessStartInfo(tempPath)
        {
            Arguments = "/SILENT /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-", // Common Inno Setup / NSIS flags
            UseShellExecute = true
        });
        
        Environment.Exit(0);
    }
}
