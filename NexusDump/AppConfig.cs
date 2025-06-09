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
}
