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
//    - [x] Save list of files inside each zip to metadata
//    - [x] Track mod IDs for mods with no files (for skip/recheck later)
//    - [x] Track mod IDs for other fail conditions (API errors, download failures, etc.)
//    - [x] Expand serialized info file to include failure reasons and timestamps
//    - [ ] Add retry mechanism for previously failed mods (waiting for CLI parser)
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
        var processedModsTracker = LoadModProcessingTracker();        ColoredLogger.LogInfo($"Starting from mod ID {config.StartingModId}, working backwards...");
        var successfulMods = processedModsTracker.GetSuccessfulModIds();
        ColoredLogger.LogInfo($"Already processed {successfulMods.Count} mods successfully");
        
        var failedMods = processedModsTracker.GetFailedMods();
        if (failedMods.Count > 0)
        {
            ColoredLogger.LogWarning($"Found {failedMods.Count} previously failed mods");
        }

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
                if (successfulMods.Contains(currentModId))
                {
                    ColoredLogger.LogDebug($"Mod {currentModId} already processed successfully, skipping...");
                    currentModId--;
                    continue;
                }

                Console.WriteLine();
                ColoredLogger.LogInfo($"Processing mod ID: {currentModId}");

                // Get mod info
                var modInfo = await GetModInfo(currentModId);                if (modInfo == null)
                {
                    ColoredLogger.LogWarning($"Mod {currentModId} not found or inaccessible");
                    TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.NotFound, "Mod not found or inaccessible");
                    SaveModProcessingTracker(processedModsTracker);
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
                    TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.NoFiles, "No files found for mod");
                    SaveModProcessingTracker(processedModsTracker);
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
                    TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.DownloadFailed, "Could not get download URL");
                    SaveModProcessingTracker(processedModsTracker);
                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Download and process the mod
                await DownloadAndProcessMod(currentModId, downloadUrl, modInfo, firstFile, processedModsTracker);
                
                // Mark as processed successfully
                TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.Success, null);
                SaveModProcessingTracker(processedModsTracker);
                ColoredLogger.LogSuccess($"Successfully processed mod {currentModId}");
                consecutiveErrors = 0;
                processedCount++;
            }
            catch (Exception ex)
            {
                ColoredLogger.LogError($"Error processing mod {currentModId}: {ex.Message}");
                TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.UnknownError, ex.Message);
                SaveModProcessingTracker(processedModsTracker);
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

    private static async Task DownloadAndProcessMod(int modId, string downloadUrl, ModInfo modInfo, ModFile modFile, ModProcessingTracker tracker)
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

        // Extract zip file and collect file list
        var extractPath = modDirectory;
        Directory.CreateDirectory(extractPath);
        var extractedFiles = new List<string>();
        
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Name)) // Skip directories
                    {
                        extractedFiles.Add(entry.FullName);
                    }
                }
            }
            
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            ColoredLogger.LogSuccess($"Extracted zip file - {extractedFiles.Count} files found");

            // Delete .archive files if configured
            if (config.DeleteArchiveFiles)
            {
                var archiveFiles = Directory.GetFiles(extractPath, "*.archive", SearchOption.AllDirectories);
                foreach (var archiveFile in archiveFiles)
                {
                    File.Delete(archiveFile);
                    ColoredLogger.LogInfo($"Deleted archive file: {Path.GetFileName(archiveFile)}");
                    
                    // Remove from extracted files list too
                    var relativePath = Path.GetRelativePath(extractPath, archiveFile);
                    extractedFiles.RemoveAll(f => f.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
                }

                ColoredLogger.LogInfo($"Removed {archiveFiles.Length} .archive files");
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error extracting zip: {ex.Message}");
            TrackModProcessing(tracker, modId, ModProcessingResult.ExtractionFailed, $"Error extracting zip: {ex.Message}");
            throw; // Re-throw to be caught by the main loop
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
            file_size = modFile.size_kb,
            extracted_files = extractedFiles,
            processed_at = DateTime.UtcNow
        }; var metadataPath = Path.Combine(modDirectory, "metadata.json");
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadataJson);

        ColoredLogger.LogSuccess("Created metadata file");
    }

    private static ModProcessingTracker LoadModProcessingTracker()
    {
        try
        {
            var filePath = Path.Combine(config.OutputDirectory, "mod_processing_status.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ModProcessingTracker>(json) ?? new ModProcessingTracker();
            }
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error loading mod processing tracker: {ex.Message}");
        }

        return new ModProcessingTracker();
    }

    private static void SaveModProcessingTracker(ModProcessingTracker tracker)
    {
        try
        {
            tracker.LastUpdated = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(tracker, new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(config.OutputDirectory, "mod_processing_status.json");
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            ColoredLogger.LogError($"Error saving mod processing tracker: {ex.Message}");
        }
    }

    private static void TrackModProcessing(ModProcessingTracker tracker, int modId, ModProcessingResult result, string? failureReason)
    {
        // Remove any existing entry for this mod
        tracker.ProcessedMods.RemoveAll(m => m.ModId == modId);
        
        // Add new entry
        tracker.ProcessedMods.Add(new ModProcessingStatus
        {
            ModId = modId,
            Result = result,
            FailureReason = failureReason,
            ProcessedAt = DateTime.UtcNow,
            RetryCount = 0
        });
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

    private static void RetryFailedMods(ModProcessingTracker tracker, int maxRetries = 3)
    {
        var failedMods = tracker.GetFailedMods()
            .Where(m => m.RetryCount < maxRetries)
            .OrderBy(m => m.ModId)
            .ToList();
            
        if (failedMods.Count == 0)
        {
            ColoredLogger.LogInfo("No failed mods available for retry");
            return;
        }
        
        ColoredLogger.LogInfo($"Found {failedMods.Count} failed mods eligible for retry");
        
        foreach (var failedMod in failedMods)
        {
            ColoredLogger.LogInfo($"Retrying mod {failedMod.ModId} (attempt #{failedMod.RetryCount + 1}, last failure: {failedMod.FailureReason})");
            
            // Increment retry count
            failedMod.RetryCount++;
            failedMod.ProcessedAt = DateTime.UtcNow;
            
            // Note: The actual retry logic would be integrated into the main processing loop
            // For now, we just update the retry count and save the tracker
        }
        
        SaveModProcessingTracker(tracker);
    }

    private static void ShowModProcessingStats(ModProcessingTracker tracker)
    {
        var totalProcessed = tracker.ProcessedMods.Count;
        var successful = tracker.ProcessedMods.Count(m => m.Result == ModProcessingResult.Success);
        var failed = totalProcessed - successful;
        
        ColoredLogger.LogHeader("Mod Processing Statistics");
        ColoredLogger.LogHeader("========================");
        ColoredLogger.LogInfo($"Total mods processed: {totalProcessed}");
        ColoredLogger.LogSuccess($"Successful: {successful}");
        
        if (failed > 0)
        {
            ColoredLogger.LogError($"Failed: {failed}");
            
            var failureGroups = tracker.ProcessedMods
                .Where(m => m.Result != ModProcessingResult.Success)
                .GroupBy(m => m.Result)
                .OrderByDescending(g => g.Count());
                
            foreach (var group in failureGroups)
            {
                ColoredLogger.LogWarning($"  {group.Key}: {group.Count()}");
            }
        }
        
        if (tracker.LastUpdated != default)
        {
            ColoredLogger.LogInfo($"Last updated: {tracker.LastUpdated:yyyy-MM-dd HH:mm:ss} UTC");
        }
    }
}
