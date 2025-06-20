using ConsoleAppFramework;
using NexusDump.Models;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text.Json;

namespace NexusDump;

// TODO (2025-06-10):
// 
// === HIGH PRIORITY ===
// 1. API Optimization & Efficiency
//    - [x] Optimize Nexus API calls: Make only one call per mod if possible - implemented pre-filtering
//    - [x] Skip mod metadata if too heavy/unnecessary - configurable via CollectFullMetadata
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
//    - [x] Integrate ConsoleAppFramework command parser
//
// === MEDIUM PRIORITY ===
// 4. File Processing & Filtering
//    - [x] Skip RAR mods (add RAR detection) - replaced with extension-based filtering
//    - [x] Delete DLL files from mods (configurable)
//    - [x] Add file type filtering (e.g., only extract specific extensions)
//    - [x] Implement mod validation (check for required files/structure) - skipped per request



class Program
{
    private static readonly HttpClient httpClient = new();
    private static readonly string baseUrl = "https://api.nexusmods.com/v1";
    public static AppConfig config = new();
    private static readonly ApiRateLimitTracker rateLimitTracker = new();

    static async Task Main(string[] args)
    {
        var app = ConsoleAppFramework.ConsoleApp.Create();
        app.Add("", () => { });
        app.Add("dump", async (string? list = null, string? key = null, CancellationToken cancellationToken = default) => await DumpAsync(list, key));

        await app.RunAsync(args);
    }

    /// <summary>
    /// Dumps mods from NexusMods based on the provided list or starting mod ID.
    /// </summary>
    /// <param name="list"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    private static async Task DumpAsync(string? list, string? key) { 
        ColoredLogger.LogHeader("NexusMods Cyberpunk 2077 Mod Downloader");
        ColoredLogger.LogHeader("=======================================");

        // Load configuration
        config = LoadConfig();

        // Get API key from file or user input
        string? apiKey = key ?? LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ColoredLogger.LogError("API key is required. Exiting...");
            return;
        }
        else
        {
            ColoredLogger.LogSuccess("API key loaded from file.");
        }

        // Setup HTTP client
        httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "NexusDump/1.0");

        // Create output directory
        if (!string.IsNullOrWhiteSpace(config.OutputDirectory))
        {
            Directory.CreateDirectory(config.OutputDirectory);
        }

        // Load processed mods
        var processedModsTracker = LoadModProcessingTracker(); ColoredLogger.LogInfo($"Starting from mod ID {config.StartingModId}, working backwards...");
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

        // Load mods from file if provided
        List<int> modIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(list) && File.Exists(list))
        {
            ColoredLogger.LogInfo($"Loading mod IDs from file: {list}");
            var modStringIds = await File.ReadAllLinesAsync(list);
            if (modStringIds.Length == 0)
            {
                ColoredLogger.LogWarning("No mod IDs found in the provided file. Using default starting mod ID.");
            }
            else
            {
                ColoredLogger.LogInfo($"Loaded {modStringIds.Length} mod IDs from file.");
            }

            // Convert to integers and filter out invalid IDs
            modIds = modStringIds
                .Select(id => int.TryParse(id.Trim(), out var parsedId) ? parsedId : -1)
                .Where(id => id > 0)
                .ToList();

            // set the starting mod ID to the highest from the list if provided
            if (modIds.Count > 0)
            {
                config.StartingModId = modIds.Max();
                ColoredLogger.LogInfo($"Starting mod ID set to the highest from the list: {config.StartingModId}");
            }
        }

        int currentModId = config.StartingModId;
        int consecutiveErrors = 0;
        int processedCount = 0;

        while (currentModId > 0 && consecutiveErrors < config.MaxConsecutiveErrors)
        {
            // check if mod is in list file if provided
            if (modIds.Count > 0 && !modIds.Contains(currentModId))
            {
                ColoredLogger.LogDebug($"Mod ID {currentModId} not in provided list, skipping...");
                currentModId--;
                continue;
            }

            // Check if we've reached the debug limit
            if (config.MaxModsToProcess > 0 && processedCount >= config.MaxModsToProcess)
            {
                ColoredLogger.LogInfo($"Debug limit reached: processed {processedCount} mods");
                break;
            }

            // do not process mods that are failed previously
            if (failedMods.Any(m => m.ModId == currentModId ))
            {
                ColoredLogger.LogDebug($"Mod {currentModId} failed previously, skipping...");
                currentModId--;
                continue;
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

                // OPTIMIZATION: Check files first to avoid unnecessary mod info call
                var modFiles = await GetModFiles(currentModId);
                if (modFiles == null || modFiles.Length == 0)
                {
                    ColoredLogger.LogWarning($"No files found for mod {currentModId}");
                    TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.NoFiles, "No files found for mod");
                    SaveModProcessingTracker(processedModsTracker);
                    currentModId--;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Check if any file has an allowed extension (pre-filtering)
                var validFile = modFiles.FirstOrDefault(f =>
                    config.AllowedModFileExtensions.Contains(Path.GetExtension(f.file_name).ToLowerInvariant()));

                if (validFile == null)
                {
                    ColoredLogger.LogWarning($"No supported file types found for mod {currentModId}");
                    TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.SkippedUnsupportedFormat,
                        "No supported file extensions found");
                    SaveModProcessingTracker(processedModsTracker);
                    currentModId--;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Get mod info only if we need full metadata AND we have valid files
                ModInfo? modInfo = null;
                if (config.CollectFullMetadata)
                {
                    modInfo = await GetModInfo(currentModId);
                    if (modInfo == null)
                    {
                        ColoredLogger.LogWarning($"Mod {currentModId} not found or inaccessible");
                        TrackModProcessing(processedModsTracker, currentModId, ModProcessingResult.NotFound, "Mod not found or inaccessible");
                        SaveModProcessingTracker(processedModsTracker);
                        currentModId--;
                        await Task.Delay(config.RateLimitDelayMs);
                        continue;
                    }
                    ColoredLogger.LogSuccess($"Found mod: {modInfo.name}");
                }
                else
                {
                    ColoredLogger.LogSuccess($"Found mod {currentModId} with valid files (metadata collection disabled)");
                }

                // Use the first valid file
                var firstFile = validFile;
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

    private static async Task DownloadAndProcessMod(int modId, string downloadUrl, ModInfo? modInfo, ModFile modFile, ModProcessingTracker tracker)
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
        var fileName = Path.GetFileNameWithoutExtension(zipPath);
        var extractPath = Path.Combine(modDirectory, fileName);
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

            // Delete unwanted files based on configuration
            var modFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
            foreach (var file in modFiles)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (config.AllowedFileExtensions.Length > 0 && !config.AllowedFileExtensions.Contains(ext))
                {
                    ColoredLogger.LogDebug($"Deleting unsupported file: {file} ({ext})");
                    File.Delete(file);
                }
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

        // Create file metadata
        var fileMetadata = new ModFileMetadata
        {
            file_name = modFile.file_name,
            file_version = modFile.version,
            file_size = modFile.size_kb,
            extracted_files = extractedFiles,
            processed_at = DateTime.UtcNow,
        };
        var fileMetadataJson = JsonSerializer.Serialize(fileMetadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(modDirectory, $"{fileName}.json"), fileMetadataJson);

        // Create metadata file
        var metadata = new ModMetadata
        {
            mod_id = modId,

            // Only set these if we have full mod info
            name = modInfo?.name,
            summary = modInfo?.summary,
            description = modInfo?.description,
            category_id = modInfo?.category_id,
            version = modInfo?.version,
            author = modInfo?.author,
            uploaded_by = modInfo?.uploaded_by,
            created_time = modInfo?.created_time,
            updated_time = modInfo?.updated_time,
            endorsement_count = modInfo?.endorsement_count,
            download_count = modInfo?.download_count,
            tags = modInfo?.tags
        }; 
        
        var metadataPath = Path.Combine(modDirectory, "metadata.json");
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadataJson);

        ColoredLogger.LogSuccess("Created metadata file");
    }

    private static ModProcessingTracker LoadModProcessingTracker()
    {
        try
        {
            var filePath = config.ProcessedModsFile;
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
            File.WriteAllText(config.ProcessedModsFile, json);
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
}
