using System;

namespace NexusDump.Models
{
    /// <summary>
    /// Data model for NexusMods API mod files response
    /// </summary>
    public class ModFilesResponse
    {
        public ModFile[] files { get; set; } = Array.Empty<ModFile>();
    }

    /// <summary>
    /// Data model for individual mod file information
    /// </summary>
    public class ModFile
    {
        public int file_id { get; set; }
        public string name { get; set; } = "";
        public string file_name { get; set; } = "";
        public string version { get; set; } = "";
        public int size_kb { get; set; }
    }
}
