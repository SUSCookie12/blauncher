using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace BLauncher.Services;

public class DownloadProgress
{
    public string CurrentFile { get; set; } = "";
    public double Percentage { get; set; }
}

public class DownloadService
{
    private static readonly HttpClient Http = new();

    public async Task DownloadFilesAsync(IEnumerable<(string url, string path, string? sha1)> files, Action<DownloadProgress> onProgress)
    {
        var fileList = new List<(string url, string path, string? sha1)>(files);
        int total = fileList.Count, completed = 0;

        foreach (var file in fileList)
        {
            if (string.IsNullOrEmpty(file.url)) { completed++; continue; }
            if (File.Exists(file.path) && !string.IsNullOrEmpty(file.sha1) && VerifySha1(file.path, file.sha1)) { completed++; continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(file.path)!);
            onProgress?.Invoke(new DownloadProgress { CurrentFile = Path.GetFileName(file.path), Percentage = (double)completed / total * 100 });

            await File.WriteAllBytesAsync(file.path, await Http.GetByteArrayAsync(file.url));
            completed++;
        }
    }

    private bool VerifySha1(string path, string expectedSha1)
    {
        using var sha1 = SHA1.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", "").Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
    }
}
