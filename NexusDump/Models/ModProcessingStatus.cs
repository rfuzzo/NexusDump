using System;

namespace NexusDump.Models
{
    public enum ModProcessingResult
    {
        Success,
        NotFound,
        NoFiles,
        DownloadFailed,
        ExtractionFailed,
        ApiError,
        UnknownError
    }

    /// <summary>
    /// Tracks the processing status of mods including success and failure information
    /// </summary>
    public class ModProcessingStatus
    {
        public int ModId { get; set; }
        public ModProcessingResult Result { get; set; }
        public string? FailureReason { get; set; }
        public DateTime ProcessedAt { get; set; }
        public int RetryCount { get; set; } = 0;
    }

    /// <summary>
    /// Container for all mod processing statuses
    /// </summary>
    public class ModProcessingTracker
    {
        public List<ModProcessingStatus> ProcessedMods { get; set; } = new();
        public DateTime LastUpdated { get; set; }
        
        public HashSet<int> GetSuccessfulModIds()
        {
            return ProcessedMods
                .Where(m => m.Result == ModProcessingResult.Success)
                .Select(m => m.ModId)
                .ToHashSet();
        }
        
        public List<ModProcessingStatus> GetFailedMods()
        {
            return ProcessedMods
                .Where(m => m.Result != ModProcessingResult.Success)
                .ToList();
        }
    }
}
