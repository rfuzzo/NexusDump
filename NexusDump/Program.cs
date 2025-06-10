using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using NexusDump.Models;

namespace NexusDump;

// TODO (2025-06-10):
// 
// === HIGH PRIORITY ===
// 1. API Optimization & Efficiency
//    - [ ] Optimize Nexus API calls: Make only one call per mod if possible
//    - [ ] Skip mod metadata if too heavy/unnecessary
//    - [ ] Cache API responses to avoid redundant calls  
//
// 2. Enhanced Metadata & Tracking
//    - [ ] Save list of files inside each zip to metadata
//    - [ ] Track mod IDs for mods with no files (for skip/recheck later)
//    - [ ] Track mod IDs for other fail conditions (API errors, download failures, etc.)
//    - [ ] Expand serialized info file to include failure reasons and timestamps
//    - [ ] Add retry mechanism for previously failed mods
//
// 3. Command Line Interface & Commands
//    - [ ] Integrate ConsoleAppFramework command parser
//    - [ ] Add 'download' command (current functionality)
//    - [ ] Add 'resume' command to continue from last position
//    - [ ] Add 'retry-failed' command to reprocess failed mods
//    - [ ] Add 'stats' command to show download statistics
//    - [ ] Add 'cleanup' command to remove incomplete downloads
//    - [ ] Add 'list-failed' command to show failed mod IDs and reasons
//
// === MEDIUM PRIORITY ===
// 4. File Processing & Filtering
//    - [ ] Skip RAR mods (add RAR detection)
//    - [ ] Delete DLL files from mods (configurable)
//    - [ ] Add file type filtering (e.g., only extract specific extensions)
//    - [ ] Implement mod validation (check for required files/structure)
//
// 5. Improved Error Handling & Logging
//    - [ ] Better error categorization and handling
//    - [ ] Add structured logging with different log levels
//    - [ ] Create detailed error reports
//    - [ ] Add progress tracking and ETA calculations
//
// === LOW PRIORITY ===
// 6. Configuration & Features
//    - [ ] Add configuration validation
//    - [ ] Support for multiple game IDs
//    - [ ] Parallel downloads (with rate limiting)
//    - [ ] Resume interrupted downloads
//    - [ ] Add mod dependency tracking

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
            // Check if we've reached the debug limit
            if (config.MaxModsToProcess > 0 && processedCount >= config.MaxModsToProcess)
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

                Console.WriteLine();
                ColoredLogger.LogInfo($"Processing mod ID: {currentModId}");

                // Get mod info
                var modInfo = await GetModInfo(currentModId); if (modInfo == null)
                {
                    ColoredLogger.LogWarning($"Mod {currentModId} not found or inaccessible");
                    processedMods.Add(currentModId);
                    SaveProcessedMods(processedMods);
                    currentModId--;
                    //consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                ColoredLogger.LogSuccess($"Found mod: {modInfo.name}");

                // Get mod files
                var modFiles = await GetModFiles(currentModId);
                if (modFiles == null || modFiles.Length == 0)
                {
                    ColoredLogger.LogWarning($"No files found for mod {currentModId}");
                    processedMods.Add(currentModId);
                    SaveProcessedMods(processedMods);
                    currentModId--;
                    //consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }                // Download first file
                var firstFile = modFiles[0];
                ColoredLogger.LogDebug($"Downloading file: {firstFile.name}");

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
