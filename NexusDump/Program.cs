using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NexusDump;

class Program
{
    private static readonly HttpClient httpClient = new();
    private static readonly string baseUrl = "https://api.nexusmods.com/v1";
    private static AppConfig config = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("NexusMods Cyberpunk 2077 Mod Downloader");
        Console.WriteLine("=======================================");        // Load configuration
        config = LoadConfig();

        // Get API key from file or user input
        string? apiKey = LoadApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Write("Enter your NexusMods API key: ");
            apiKey = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("API key is required. Exiting...");
                return;
            }
        }
        else
        {
            Console.WriteLine("API key loaded from file.");
        }

        // Setup HTTP client
        httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "NexusDump/1.0");

        // Create output directory
        Directory.CreateDirectory(config.OutputDirectory);

        // Load processed mods
        var processedMods = LoadProcessedMods(); 
        
        Console.WriteLine($"Starting from mod ID {config.StartingModId}, working backwards...");
        Console.WriteLine($"Already processed {processedMods.Count} mods");

        if (config.MaxModsToProcess > 0)
        {
            Console.WriteLine($"Debug mode: Will process maximum {config.MaxModsToProcess} mods");
        }

        int currentModId = config.StartingModId;
        int consecutiveErrors = 0;
        int processedCount = 0;

        while (currentModId > 0 && consecutiveErrors < config.MaxConsecutiveErrors)
        {
            // Check if we've reached the debug limit
            if (config.MaxModsToProcess > 0 && processedCount >= config.MaxModsToProcess)
            {
                Console.WriteLine($"Debug limit reached: processed {processedCount} mods");
                break;
            }
            try
            {
                if (processedMods.Contains(currentModId))
                {
                    Console.WriteLine($"Mod {currentModId} already processed, skipping...");
                    currentModId--;
                    continue;
                }

                Console.WriteLine($"\nProcessing mod ID: {currentModId}");

                // Get mod info
                var modInfo = await GetModInfo(currentModId);
                if (modInfo == null)
                {
                    Console.WriteLine($"Mod {currentModId} not found or inaccessible");
                   
                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                Console.WriteLine($"Found mod: {modInfo.name}");

                // Get mod files
                var modFiles = await GetModFiles(currentModId);
                if (modFiles == null || modFiles.Length == 0)
                {
                    Console.WriteLine($"No files found for mod {currentModId}");
                   
                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Download first file
                var firstFile = modFiles[0];
                Console.WriteLine($"Downloading file: {firstFile.name}");

                var downloadUrl = await GetDownloadUrl(currentModId, firstFile.file_id);
                if (downloadUrl == null)
                {
                    Console.WriteLine($"Could not get download URL for mod {currentModId}");
                  
                    currentModId--;
                    consecutiveErrors++;
                    await Task.Delay(config.RateLimitDelayMs);
                    continue;
                }

                // Download and process the mod
                await DownloadAndProcessMod(currentModId, downloadUrl, modInfo, firstFile);

                // Mark as processed
                processedMods.Add(currentModId);
                SaveProcessedMods(processedMods); Console.WriteLine($"Successfully processed mod {currentModId}");
                consecutiveErrors = 0;
                processedCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing mod {currentModId}: {ex.Message}");
                consecutiveErrors++;
            }

            currentModId--;

            // Rate limiting
            await Task.Delay(config.RateLimitDelayMs);
        }

        if (consecutiveErrors >= config.MaxConsecutiveErrors)
        {
            Console.WriteLine($"Stopped after {config.MaxConsecutiveErrors} consecutive errors");
        }

        Console.WriteLine("Download process completed!");
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
            Console.WriteLine($"Error loading config: {ex.Message}. Using defaults.");
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
            Console.WriteLine($"Error loading API key from file: {ex.Message}");
        }

        return null;
    }

    private static async Task<ModInfo?> GetModInfo(int modId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}");
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
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}/files");
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
            var response = await httpClient.GetAsync($"{baseUrl}/games/{config.GameId}/mods/{modId}/files/{fileId}/download_link");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var downloadResponse = JsonSerializer.Deserialize<DownloadResponse>(json);
            return downloadResponse?.URI;
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

        Console.WriteLine($"Downloaded {zipPath}");

        // Extract zip file
        var extractPath = Path.Combine(modDirectory, "extracted");
        Directory.CreateDirectory(extractPath);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            Console.WriteLine("Extracted zip file");

            // Delete .archive files if configured
            if (config.DeleteArchiveFiles)
            {
                var archiveFiles = Directory.GetFiles(extractPath, "*.archive", SearchOption.AllDirectories);
                foreach (var archiveFile in archiveFiles)
                {
                    File.Delete(archiveFile);
                    Console.WriteLine($"Deleted archive file: {Path.GetFileName(archiveFile)}");
                }

                Console.WriteLine($"Removed {archiveFiles.Length} .archive files");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting zip: {ex.Message}");
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
        };

        var metadataPath = Path.Combine(modDirectory, "metadata.json");
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, metadataJson);

        Console.WriteLine("Created metadata file");
    }

    private static HashSet<int> LoadProcessedMods()
    {
        try
        {
            if (File.Exists(config.ProcessedModsFile))
            {
                var json = File.ReadAllText(config.ProcessedModsFile);
                var processedIds = JsonSerializer.Deserialize<int[]>(json);
                return new HashSet<int>(processedIds ?? Array.Empty<int>());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading processed mods: {ex.Message}");
        }

        return new HashSet<int>();
    }

    private static void SaveProcessedMods(HashSet<int> processedMods)
    {
        try
        {
            var json = JsonSerializer.Serialize(processedMods.ToArray(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(config.ProcessedModsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving processed mods: {ex.Message}");
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
