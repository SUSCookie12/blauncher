using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BLauncher.Services;

public class PresenceManager
{
    // We use one shared HttpClient for performance
    private static readonly HttpClient client = new HttpClient();
    private bool isRunning = false;
    private int _currentSessionId = 0;
    
    // Track stats for the UI
    public DateTime LastSignalTime { get; private set; } = DateTime.MinValue;
    public bool LastSignalSuccess { get; private set; } = false;
    
    // Your n8n production webhook URL
    private string webhookUrl = "https://n8n.cspack.online/webhook/launcher-heartbeat"; 

    public async Task StartHeartbeat(string currentUserId)
    {
        // Increment session ID to invalidate any previous loops
        int sessionId = ++_currentSessionId;
        isRunning = true;
        
        // This loop runs in the background as long as the launcher is open
        while (isRunning && sessionId == _currentSessionId)
        {
            try
            {
                // Create a simple JSON payload with the user's UID
                string jsonPayload = $"{{\"uid\": \"{currentUserId}\"}}";
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send the POST request to n8n
                var response = await client.PostAsync(webhookUrl, content);
                LastSignalSuccess = response.IsSuccessStatusCode;
                LastSignalTime = DateTime.Now;

                Console.WriteLine($"Heartbeat sent to n8n for user {currentUserId}! Success: {LastSignalSuccess}");
            }
            catch (Exception ex)
            {
                LastSignalSuccess = false;
                LastSignalTime = DateTime.Now;
                // If they lose internet connection momentarily, it won't crash the launcher
                Console.WriteLine($"Heartbeat failed: {ex.Message}");
            }

            // Wait for 90 seconds (1.5 minutes) before sending the next ping
            try {
                await Task.Delay(TimeSpan.FromSeconds(90)); 
            } catch { break; }
        }
    }

    // Call this if the user manually logs out
    public void StopHeartbeat()
    {
        isRunning = false; 
        _currentSessionId++; // Invalidate any running loop
    }
}
