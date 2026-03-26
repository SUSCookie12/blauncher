using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Collections.Generic;

namespace BLauncher.Services;

public class AuthSession
{
    [JsonProperty("localId")]
    public string? LocalId { get; set; }
    
    [JsonProperty("idToken")]
    public string? IdToken { get; set; }
    
    [JsonProperty("refreshToken")]
    public string? RefreshToken { get; set; }
    
    [JsonProperty("expiresAt")]
    public DateTime ExpiresAt { get; set; }
    
    [JsonProperty("username")]
    public string? MinecraftUsername { get; set; }
    
    [JsonProperty("skinUrl")]
    public string? SkinUrl { get; set; }
    
    [JsonProperty("email")]
    public string? Email { get; set; }
    
    [JsonProperty("isComplete")]
    public bool IsProfileComplete { get; set; }

    [JsonProperty("balance")]
    public double Balance { get; set; }

    [JsonProperty("createdAt")]
    public string? CreatedAt { get; set; }
}

public class AuthService
{
    private const string ApiKey = ".";
    private const string ProjectId = "cookiemovie-27669";
    private const string FirestoreBase = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";
    private const string LoginTokenUrl = $"{FirestoreBase}/login_tokens";
    private const string AuthUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key={ApiKey}";
    private static readonly HttpClient Http = new();
    private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BLauncher");
    private static readonly string TokenFile = Path.Combine(AppDataDir, "auth.json");
    public static readonly string SkinFile = Path.Combine(AppDataDir, "skin.png");

    public AuthSession? CurrentSession { get; private set; }

    public AuthService()
    {
        Directory.CreateDirectory(AppDataDir);
        TryAutoLogin();
    }

    private void TryAutoLogin()
    {
        try
        {
            if (File.Exists(TokenFile))
            {
                var json = File.ReadAllText(TokenFile);
                CurrentSession = JsonConvert.DeserializeObject<AuthSession>(json);
                if (CurrentSession != null && (string.IsNullOrEmpty(CurrentSession.LocalId) || string.IsNullOrEmpty(CurrentSession.IdToken)))
                {
                    if (!string.IsNullOrEmpty(CurrentSession.IdToken) && string.IsNullOrEmpty(CurrentSession.LocalId))
                    {
                        var profile = ExtractProfileFromToken(CurrentSession.IdToken);
                        CurrentSession.LocalId = profile?.uid;
                    }
                    if (string.IsNullOrEmpty(CurrentSession.LocalId)) CurrentSession = null;
                }
            }
        }
        catch { }
    }

    public async Task<bool> LoginWithCSPackageAsync(Action<string> onStatusUpdate)
    {
        try
        {
            onStatusUpdate("Initializing secure login...");
            var payload = new { fields = new {
                createdAt = new { timestampValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                token = new { stringValue = "" },
                status = new { stringValue = "waiting" }
            }};
            var response = await Http.PostAsync(LoginTokenUrl, new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();
            var docData = JObject.Parse(await response.Content.ReadAsStringAsync());
            string docId = docData["name"]?.ToString().Split('/').Last() ?? throw new Exception("Doc error");
            Process.Start(new ProcessStartInfo($"https://cspack.online/request?api={docId}") { UseShellExecute = true });
            onStatusUpdate("Browser opened...");
            string? customToken = null;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(5000);
                var getResponse = await Http.GetAsync($"{LoginTokenUrl}/{docId}");
                if (!getResponse.IsSuccessStatusCode) continue;
                var currentDoc = JObject.Parse(await getResponse.Content.ReadAsStringAsync());
                if (currentDoc["fields"]?["status"]?["stringValue"]?.ToString() == "completed")
                {
                    customToken = currentDoc["fields"]?["token"]?["stringValue"]?.ToString();
                    break;
                }
            }
            if (string.IsNullOrEmpty(customToken)) throw new Exception("Timed out.");
            onStatusUpdate("Authenticating...");
            var authResponse = await Http.PostAsync(AuthUrl, new StringContent(JsonConvert.SerializeObject(new { token = customToken, returnSecureToken = true }), Encoding.UTF8, "application/json"));
            var authBody = await authResponse.Content.ReadAsStringAsync();
            if (!authResponse.IsSuccessStatusCode) throw new Exception(authBody);

            var authData = JObject.Parse(authBody);
            string? idToken = authData["idToken"]?.ToString();
            var jwtProfile = ExtractProfileFromToken(idToken!);
            
            CurrentSession = new AuthSession
            {
                LocalId = (authData["localId"] ?? authData["uid"])?.ToString() ?? jwtProfile?.uid,
                IdToken = idToken,
                RefreshToken = authData["refreshToken"]?.ToString(),
                ExpiresAt = DateTime.UtcNow.AddSeconds(double.Parse(authData["expiresIn"]?.ToString() ?? "3600"))
            };

            await ValidateProfileAsync(onStatusUpdate);
            SaveSession();
            return true;
        }
        catch (Exception ex) { onStatusUpdate($"Error: {ex.Message}"); return false; }
    }

    private (string? uid, string? email)? ExtractProfileFromToken(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var obj = JObject.Parse(json);
            return (obj["sub"]?.ToString() ?? obj["user_id"]?.ToString(), obj["email"]?.ToString());
        }
        catch { return null; }
    }

    public async Task<bool> ValidateProfileAsync(Action<string>? onStatusUpdate = null)
    {
        if (CurrentSession == null || string.IsNullOrEmpty(CurrentSession.IdToken)) return false;
        onStatusUpdate?.Invoke("Syncing official account details...");
        try
        {
            // 1. Fetch main user document for Email
            string mainUrl = $"{FirestoreBase}/users/{CurrentSession.LocalId}";
            using var mainRequest = new HttpRequestMessage(HttpMethod.Get, mainUrl);
            mainRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CurrentSession.IdToken);
            var mainResponse = await Http.SendAsync(mainRequest);
            if (mainResponse.IsSuccessStatusCode)
            {
                var mainDoc = JObject.Parse(await mainResponse.Content.ReadAsStringAsync());
                var mainFields = mainDoc["fields"] as JObject;
                CurrentSession.Email = GetFieldString(mainFields, "email");
                CurrentSession.Balance = GetFieldDouble(mainFields, "balance");
                CurrentSession.CreatedAt = GetFieldString(mainFields, "createdAt");
            }

            // 2. Fetch Minecraft subdocument
            string mcUrl = $"{FirestoreBase}/users/{CurrentSession.LocalId}/blauncher/main";
            using var mcRequest = new HttpRequestMessage(HttpMethod.Get, mcUrl);
            mcRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CurrentSession.IdToken);
            var mcResponse = await Http.SendAsync(mcRequest);
            
            if (mcResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                CurrentSession.IsProfileComplete = false;
                SaveSession();
                return false;
            }
            mcResponse.EnsureSuccessStatusCode();
            var doc = JObject.Parse(await mcResponse.Content.ReadAsStringAsync());
            var fields = doc["fields"] as JObject;
            if (fields == null) return false;

            CurrentSession.MinecraftUsername = GetFieldString(fields, "username");
            CurrentSession.Email = GetFieldString(fields, "email");
            CurrentSession.Balance = GetFieldDouble(fields, "balance");
            CurrentSession.SkinUrl = GetFieldString(fields, "skinUrl");
            bool hasCreatedAt = fields.ContainsKey("createdAt") || fields.ContainsKey("createdAtTime");
            
            CurrentSession.IsProfileComplete = !string.IsNullOrEmpty(CurrentSession.MinecraftUsername) && !string.IsNullOrEmpty(CurrentSession.SkinUrl) && hasCreatedAt;
            
            if (CurrentSession.IsProfileComplete && !string.IsNullOrEmpty(CurrentSession.SkinUrl))
            {
                onStatusUpdate?.Invoke("Fetching updated skin data...");
                SaveSkinToDisk(CurrentSession.SkinUrl);
            }

            SaveSession();
            return CurrentSession.IsProfileComplete;
        }
        catch { return false; }
    }

    private void SaveSkinToDisk(string dataUri)
    {
        try
        {
            if (!dataUri.Contains("base64,")) return;
            string base64Data = dataUri.Split(',')[1];
            byte[] imageBytes = Convert.FromBase64String(base64Data);
            File.WriteAllBytes(SkinFile, imageBytes);
        }
        catch { }
    }

    private string? GetFieldString(JObject? fields, string key)
    {
        if (fields == null) return null;
        if (fields.TryGetValue(key, out var field)) {
            return field["stringValue"]?.ToString() 
                ?? field["timestampValue"]?.ToString() 
                ?? field.ToString(Formatting.None).Trim('"');
        }
        return null;
    }

    private double GetFieldDouble(JObject? fields, string key)
    {
        if (fields == null) return 0;
        if (fields.TryGetValue(key, out var field)) {
            string? val = field["doubleValue"]?.ToString() ?? field["integerValue"]?.ToString() ?? field.ToString();
            if (double.TryParse(val, out double result)) return result;
        }
        return 0;
    }

    private void SaveSession()
    {
        if (CurrentSession == null) return;
        File.WriteAllText(TokenFile, JsonConvert.SerializeObject(CurrentSession));
    }

    public void LogoutAsync() { 
        CurrentSession = null; 
        if (File.Exists(TokenFile)) File.Delete(TokenFile); 
        if (File.Exists(SkinFile)) File.Delete(SkinFile);
    }
    public void Logout() => LogoutAsync();
}
