namespace NexusDump
{
    /// <summary>
    /// Configuration class for the NexusMods downloader application
    /// </summary>
    public class AppConfig
    {
        public int StartingModId { get; set; } = 21959;
        public int RateLimitDelayMs { get; set; } = 1000;
        public int MaxConsecutiveErrors { get; set; } = 10;
        public string OutputDirectory { get; set; } = "";
        public string ProcessedModsFile { get; set; } = "mod_processing_status.json";
        public string GameId { get; set; } = "cyberpunk2077";
        public bool DeleteOriginalZip { get; set; } = true;
        public string[] AllowedModFileExtensions { get; set; } = { ".zip" }; // Only these file types will be downloaded as mods
        public string[] AllowedFileExtensions { get; set; } = { ".reds", ".lua", ".json", ".tweak", ".txt", ".md", ".xl", ".wscript", ".xml", ".yaml" }; // These file types will be allowed after extraction
        public bool CollectFullMetadata { get; set; } = true; // If false, skip heavy mod info API call (saves 1 API call per mod)
        public int MaxModsToProcess { get; set; } = -1; // -1 = unlimited, any positive number = limit for debugging
        public int MinHourlyCallsRemaining { get; set; } = 10; // Wait if hourly calls drop below this
        public int MinDailyCallsRemaining { get; set; } = 50; // Wait if daily calls drop below this
        public int RateLimitWaitMinutes { get; set; } = 10; // How long to wait when rate limit is hit
    }
}
