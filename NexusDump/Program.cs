using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NexusDump;

// Colored logger for better user experience
public static class ColoredLogger
{
    public static void LogInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"ℹ️  {message}");
        Console.ResetColor();
    }

    public static void LogSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ {message}");
        Console.ResetColor();
    }

    public static void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠️  {message}");
        Console.ResetColor();
    }

    public static void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ {message}");
        Console.ResetColor();
    }

    public static void LogDebug(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"🐛 {message}");
        Console.ResetColor();
    }

    public static void LogProgress(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"🔄 {message}");
        Console.ResetColor();
    }

    public static void LogDownload(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"⬇️  {message}");
        Console.ResetColor();
    }

    public static void LogApiLimit(string message)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"📊 {message}");
        Console.ResetColor();
    }

    public static void LogHeader(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void LogRateLimit(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"⏱️  {message}");
        Console.ResetColor();
    }
}

class Program
{
    private static readonly HttpClient httpClient = new();
    private static readonly string baseUrl = "https://api.nexusmods.com/v1";
    public static AppConfig config = new();
    private static ApiRateLimitTracker rateLimitTracker = new();

    static async Task Main(string[] args)
    {
        ColoredLogger.LogHeader("NexusMods Cyberpunk 2077 Mod Downloader");
        ColoredLogger.LogHeader("=======================================");

        // Load configuration
        config = LoadConfig();

        // Get API key from file or user input
        string? apiKey = LoadApiKey(); if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("Enter your NexusMods API key: ");
            Console.ResetColor();
            apiKey = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ColoredLogger.LogError("API key is required. Exiting...");
                return;
            }
        }
        else
        {
            ColoredLogger.LogSuccess("API key loaded from file.");
        }

        // Setup HTTP client
        httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "NexusDump/1.0");

        // Create output directory
        Directory.CreateDirectory(config.OutputDirectory);

        // Load processed mods
        var processedMods = LoadProcessedMods(); ColoredLogger.LogInfo($"Starting from mod ID {config.StartingModId}, working backwards...");
        ColoredLogger.LogInfo($"Already processed {processedMods.Count} mods");

        if (config.MaxModsToProcess > 0)
        {
            ColoredLogger.LogDebug($"Debug mode: Will process maximum {config.MaxModsToProcess} mods");
        }

        int currentModId = config.StartingModId;
        int consecutiveErrors = 0;
        int processedCount = 0;

        while (currentModId > 0 && consecutiveErrors < config.MaxConsecutiveErrors)
        {
            // Check if we've reached the debug limit            if (config.MaxModsToProcess > 0 && processedCount >= config.MaxModsToProcess)
            {
                ColoredLogger.LogInfo($"Debug limit reached: processed {processedCount} mods");
                break;
            }
            try
            {
                if (processedMods.Contains(currentModId))
                {
                    ColoredLogger.LogDebug($"Mod {currentModId} already processed, skipping...");
                    currentModId--;
                    continue;
                }

                ColoredLogger.LogProgress($"Processing mod ID: {currentModId}");

                // Get mod info
                var modInfo = await GetModInfo(currentModId); if (modInfo == null)
                {
                    ColoredLogger.LogWarning($"Mod {currentModId} not found or inaccessible");

                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                ColoredLogger.LogSuccess($"Found mod: {modInfo.name}");

                // Get mod files
                var modFiles = await GetModFiles(currentModId);
                if (modFiles == null || modFiles.Length == 0)
                {
                    ColoredLogger.LogWarning($"No files found for mod {currentModId}");

                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }                // Download first file
                var firstFile = modFiles[0];
                ColoredLogger.LogDownload($"Downloading file: {firstFile.name}");

                var downloadUrl = await GetDownloadUrl(currentModId, firstFile.file_id);
                if (downloadUrl == null)
                {
                    ColoredLogger.LogError($"Could not get download URL for mod {currentModId}");

                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Download and process the mod
                await DownloadAndProcessMod(currentModId, downloadUrl, modInfo, firstFile);                // Mark as processed
                processedMods.Add(currentModId);
                SaveProcessedMods(processedMods);
                ColoredLogger.LogSuccess($"Successfully processed mod {currentModId}");
                consecutiveErrors = 0;
                processedCount++;
            }
            catch (Exception ex)
            {
                ColoredLogger.LogError($"Error processing mod {currentModId}: {ex.Message}");
                consecutiveErrors++;
            }

            currentModId--;

            // Rate limiting
            await Task.Delay(config.RateLimitDelayMs);
        }
        if (consecutiveErrors >= config.MaxConsecutiveErrors)
        {
            ColoredLogger.LogError($"Stopped after {config.MaxConsecutiveErrors} consecutive errors");
        }

        ColoredLogger.LogSuccess("Download process completed!");
    }

    private static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists("config.json"))
            {
                var json = File.ReadAllText("config.json");
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogWarning($"Error loading config: {ex.Message}. Using defaults.");
        }
        return new AppConfig();
    }

    private static string? LoadApiKey()
    {
        try
        {
            if (File.Exists("apikey.txt"))
            {
                var apiKey = File.ReadAllText("apikey.txt").Trim();
                return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error loading API key from file: {ex.Message}");
        }

        return null;
    }

    private static async Task<ModInfo?> GetModInfo(int modId)
    {
        try
        {
            await rateLimitTracker.WaitIfNeeded();
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}");
            rateLimitTracker.UpdateFromResponse(response);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ModInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ModFile[]?> GetModFiles(int modId)
    {
        try
        {
            await rateLimitTracker.WaitIfNeeded();
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}/files");
            rateLimitTracker.UpdateFromResponse(response);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var filesResponse = JsonSerializer.Deserialize<ModFilesResponse>(json);
            return filesResponse?.files;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetDownloadUrl(int modId, int fileId)
    {
        try
        {
            await rateLimitTracker.WaitIfNeeded();
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}/files/{fileId}/download_link");
            rateLimitTracker.UpdateFromResponse(response);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var downloadResponse = JsonSerializer.Deserialize<List<DownloadResponse>>(json);

            // get the first
            if (downloadResponse == null || downloadResponse.Count == 0)
            {
                return null;
            }

            // Return the URI of the first download link
            if (string.IsNullOrWhiteSpace(downloadResponse[0].URI))
            {
                return null;
            }            // Return the first valid download URL
            ColoredLogger.LogDebug($"Download URL for mod {modId}, file {fileId}: {downloadResponse[0].URI}");
            // Return the URI of the first download link
            return downloadResponse[0].URI;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DownloadAndProcessMod(int modId, string downloadUrl, ModInfo modInfo, ModFile modFile)
    {
        var modDirectory = Path.Combine(config.OutputDirectory, modId.ToString());
        Directory.CreateDirectory(modDirectory);

        // Download the file
        var zipPath = Path.Combine(modDirectory, $"{modFile.file_name}");

        using (var response = await httpClient.GetAsync(downloadUrl))
        {
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(zipPath);
            await response.Content.CopyToAsync(fileStream);
        }

        ColoredLogger.LogSuccess($"Downloaded {zipPath}");

        // Extract zip file
        var extractPath = modDirectory;
        Directory.CreateDirectory(extractPath); try
        {
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            ColoredLogger.LogSuccess("Extracted zip file");

            // Delete .archive files if configured
            if (config.DeleteArchiveFiles)
            {
                var archiveFiles = Directory.GetFiles(extractPath, "*.archive", SearchOption.AllDirectories); foreach (var archiveFile in archiveFiles)
                {
                    File.Delete(archiveFile);
                    ColoredLogger.LogInfo($"Deleted archive file: {Path.GetFileName(archiveFile)}");
                }

                ColoredLogger.LogInfo($"Removed {archiveFiles.Length} .archive files");
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error extracting zip: {ex.Message}");
        }

        // Delete the original zip file if configured
        if (config.DeleteOriginalZip)
        {
            File.Delete(zipPath);
        }

        // Create metadata file
        var metadata = new ModMetadata
        {
            mod_id = modId,
            name = modInfo.name,
            summary = modInfo.summary,
            description = modInfo.description,
            category_id = modInfo.category_id,
            version = modInfo.version,
            author = modInfo.author,
            uploaded_by = modInfo.uploaded_by,
            created_time = modInfo.created_time,
            updated_time = modInfo.updated_time,
            endorsement_count = modInfo.endorsement_count,
            download_count = modInfo.download_count,
            tags = modInfo.tags,
            file_name = modFile.file_name,
            file_version = modFile.version,
            file_size = modFile.size_kb
        }; var metadataPath = Path.Combine(modDirectory, "metadata.json");
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadataJson);

        ColoredLogger.LogSuccess("Created metadata file");
    }

    private static HashSet<int> LoadProcessedMods()
    {
        try
        {
            var filePath = Path.Combine(config.OutputDirectory, config.ProcessedModsFile);
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var processedIds = JsonSerializer.Deserialize<int[]>(json);
                return new HashSet<int>(processedIds ?? Array.Empty<int>());
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error loading processed mods: {ex.Message}");
        }

        return new HashSet<int>();
    }

    private static void SaveProcessedMods(HashSet<int> processedMods)
    {
        try
        {
            var json = JsonSerializer.Serialize(processedMods.ToArray(), new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(config.OutputDirectory, config.ProcessedModsFile);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error saving processed mods: {ex.Message}");
        }
    }
}

// Configuration class
public class AppConfig
{
    public int StartingModId { get; set; } = 21959;
    public int RateLimitDelayMs { get; set; } = 1000;
    public int MaxConsecutiveErrors { get; set; } = 10;
    public string OutputDirectory { get; set; } = "downloaded_mods";
    public string ProcessedModsFile { get; set; } = "processed_mods.json";
    public string GameId { get; set; } = "cyberpunk2077";
    public bool DeleteArchiveFiles { get; set; } = true;
    public bool DeleteOriginalZip { get; set; } = true;
    public int MaxModsToProcess { get; set; } = -1; // -1 = unlimited, any positive number = limit for debugging
    public int MinHourlyCallsRemaining { get; set; } = 10; // Wait if hourly calls drop below this
    public int MinDailyCallsRemaining { get; set; } = 50; // Wait if daily calls drop below this
    public int RateLimitWaitMinutes { get; set; } = 60; // How long to wait when rate limit is hit
}

// Data models for NexusMods API responses
public class ModInfo
{
    public int mod_id { get; set; }
    public string name { get; set; } = "";
    public string summary { get; set; } = "";
    public string description { get; set; } = "";
    public int category_id { get; set; }
    public string version { get; set; } = "";
    public string author { get; set; } = "";
    public string uploaded_by { get; set; } = "";
    public string created_time { get; set; } = "";
    public string updated_time { get; set; } = "";
    public int endorsement_count { get; set; }
    public int download_count { get; set; }
    public ModTag[] tags { get; set; } = Array.Empty<ModTag>();
}

public class ModTag
{
    public string name { get; set; } = "";
}

public class ModFilesResponse
{
    public ModFile[] files { get; set; } = Array.Empty<ModFile>();
}

public class ModFile
{
    public int file_id { get; set; }
    public string name { get; set; } = "";
    public string file_name { get; set; } = "";
    public string version { get; set; } = "";
    public int size_kb { get; set; }
}

public class DownloadResponse
{
    public string URI { get; set; } = "";
}

public class ModMetadata
{
    public int mod_id { get; set; }
    public string name { get; set; } = "";
    public string summary { get; set; } = "";
    public string description { get; set; } = "";
    public int category_id { get; set; }
    public string version { get; set; } = "";
    public string author { get; set; } = "";
    public string uploaded_by { get; set; } = "";
    public string created_time { get; set; } = "";
    public string updated_time { get; set; } = "";
    public int endorsement_count { get; set; }
    public int download_count { get; set; }
    public ModTag[] tags { get; set; } = Array.Empty<ModTag>();
    public string file_name { get; set; } = "";
    public string file_version { get; set; } = "";
    public int file_size { get; set; }
}

// Rate limiting tracker for NexusMods API
public class ApiRateLimitTracker
{
    private int? _dailyRemaining;
    private int? _hourlyRemaining;
    private DateTime? _dailyReset;
    private DateTime? _hourlyReset;
    private DateTime _lastCall = DateTime.MinValue;
    private readonly object _lock = new object();

    public void UpdateFromResponse(HttpResponseMessage response)
    {
        lock (_lock)
        {
            _lastCall = DateTime.UtcNow;

            // Extract rate limit headers - NexusMods uses these header names
            if (response.Headers.TryGetValues("x-rl-daily-remaining", out var dailyRemainingValues))
            {
                if (int.TryParse(dailyRemainingValues.FirstOrDefault(), out var dailyRemaining))
                {
                    _dailyRemaining = dailyRemaining;
                }
            }

            if (response.Headers.TryGetValues("x-rl-hourly-remaining", out var hourlyRemainingValues))
            {
                if (int.TryParse(hourlyRemainingValues.FirstOrDefault(), out var hourlyRemaining))
                {
                    _hourlyRemaining = hourlyRemaining;
                }
            }

            if (response.Headers.TryGetValues("x-rl-daily-reset", out var dailyResetValues))
            {
                if (long.TryParse(dailyResetValues.FirstOrDefault(), out var dailyResetTimestamp))
                {
                    _dailyReset = DateTimeOffset.FromUnixTimeSeconds(dailyResetTimestamp).DateTime;
                }
            }

            if (response.Headers.TryGetValues("x-rl-hourly-reset", out var hourlyResetValues))
            {
                if (long.TryParse(hourlyResetValues.FirstOrDefault(), out var hourlyResetTimestamp))
                {
                    _hourlyReset = DateTimeOffset.FromUnixTimeSeconds(hourlyResetTimestamp).DateTime;
                }
            }

            LogRateLimitStatus();
        }
    }

    public async Task WaitIfNeeded()
    {
        lock (_lock)
        {
            // Always wait at least the configured delay between calls
            var timeSinceLastCall = DateTime.UtcNow - _lastCall;
            var minDelay = TimeSpan.FromMilliseconds(Program.config.RateLimitDelayMs); if (timeSinceLastCall < minDelay)
            {
                var waitTime = minDelay - timeSinceLastCall;
                ColoredLogger.LogRateLimit($"Rate limiting: waiting {waitTime.TotalMilliseconds:F0}ms...");
                Thread.Sleep(waitTime);
            }

            // Check if we need to wait due to low API call limits
            var needsWait = false;
            var waitMessage = "";

            if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
            {
                needsWait = true;
                var resetTime = _hourlyReset ?? DateTime.UtcNow.AddHours(1);
                var waitTime = resetTime - DateTime.UtcNow;
                waitMessage = $"Hourly API limit low ({_hourlyRemaining} remaining). Waiting until {resetTime:HH:mm:ss UTC} ({waitTime.TotalMinutes:F1} minutes)";
            }
            else if (_dailyRemaining.HasValue && _dailyRemaining.Value <= Program.config.MinDailyCallsRemaining)
            {
                needsWait = true;
                var resetTime = _dailyReset ?? DateTime.UtcNow.AddDays(1);
                var waitTime = resetTime - DateTime.UtcNow;
                waitMessage = $"Daily API limit low ({_dailyRemaining} remaining). Waiting until {resetTime:yyyy-MM-dd HH:mm:ss UTC} ({waitTime.TotalHours:F1} hours)";
            }
            if (needsWait)
            {
                ColoredLogger.LogWarning("API Rate Limit Warning");
                ColoredLogger.LogWarning(waitMessage);
                ColoredLogger.LogInfo($"You can adjust MinHourlyCallsRemaining ({Program.config.MinHourlyCallsRemaining}) and MinDailyCallsRemaining ({Program.config.MinDailyCallsRemaining}) in config.json");
                ColoredLogger.LogInfo("Press Ctrl+C to stop or wait for automatic resume...");
            }
        }

        if (CheckNeedsLongWait())
        {
            await WaitForReset();
        }
    }

    private bool CheckNeedsLongWait()
    {
        lock (_lock)
        {
            if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
                return true;
            if (_dailyRemaining.HasValue && _dailyRemaining.Value <= Program.config.MinDailyCallsRemaining)
                return true;
            return false;
        }
    }

    private async Task WaitForReset()
    {
        DateTime waitUntil;
        string waitType;

        lock (_lock)
        {
            if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
            {
                waitUntil = _hourlyReset ?? DateTime.UtcNow.AddHours(1);
                waitType = "hourly";
            }
            else
            {
                waitUntil = _dailyReset ?? DateTime.UtcNow.AddDays(1);
                waitType = "daily";
            }
        } while (DateTime.UtcNow < waitUntil)
        {
            var remaining = waitUntil - DateTime.UtcNow;
            ColoredLogger.LogRateLimit($"Waiting for {waitType} reset... {remaining.TotalMinutes:F1} minutes remaining");

            // Wait in smaller chunks so we can show progress
            var waitTime = remaining.TotalMinutes > 5 ? TimeSpan.FromMinutes(5) : remaining;
            await Task.Delay(waitTime);
        }

        ColoredLogger.LogSuccess($"{waitType} rate limit reset! Resuming operations...");
    }

    private void LogRateLimitStatus()
    {
        var status = new List<string>();

        if (_hourlyRemaining.HasValue)
            status.Add($"Hourly: {_hourlyRemaining} remaining");
        if (_dailyRemaining.HasValue)
            status.Add($"Daily: {_dailyRemaining} remaining"); if (status.Any())
        {
            ColoredLogger.LogApiLimit($"API Limits - {string.Join(", ", status)}");
        }
    }
}
