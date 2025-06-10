using System;

namespace NexusDump.Models
{
    /// <summary>
    /// Metadata model for storing comprehensive mod information locally
    /// </summary>
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
        
        // New fields for enhanced metadata
        public List<string> extracted_files { get; set; } = new();
        public DateTime processed_at { get; set; }
    }
}
