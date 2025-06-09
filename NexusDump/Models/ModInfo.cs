using System;

namespace NexusDump.Models
{
    /// <summary>
    /// Data model for NexusMods API mod information response
    /// </summary>
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

    /// <summary>
    /// Data model for mod tags
    /// </summary>
    public class ModTag
    {
        public string name { get; set; } = "";
    }
}
