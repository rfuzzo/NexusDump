# NexusDump

A C# command-line tool for downloading and processing Cyberpunk 2077 mods from NexusMods. This tool downloads mods from a curated list, extracts and filters files automatically.

## Features

- **Automated Mod Downloading**: Downloads mods from NexusMods API using a curated list
- **Smart File Filtering**: Only keeps specific file types (`.reds`, `.lua`, `.json`, `.tweak`, `.txt`, `.md`, `.xl`, `.wscript`, `.xml`, `.yaml`)
- **API Rate Limiting**: Respects NexusMods API limits with configurable delays
- **Resume Support**: Tracks processed mods to avoid re-downloading
- **Metadata Collection**: Saves detailed mod information and file lists
- **Batch Processing**: Can process thousands of mods from a predefined list

## Quick Start

1. **Setup**:
   ```bash
   # Clone the repository
   git clone <repository-url>
   cd NexusDump
   
   # Build the project
   dotnet build
   ```

2. **Get NexusMods API Key**:
   - Visit [NexusMods.com](https://www.nexusmods.com) and log in
   - Go to your profile settings → API Keys
   - Generate a new API key
   - Copy `apikey.txt.example` to `apikey.txt` and paste your key

3. **Download Mods**:
   ```bash
   # Download mods from the curated list
   dotnet run -- dump --list data/mod_ids.txt
   
   # Or specify API key manually
   dotnet run -- dump --list data/mod_ids.txt --key YOUR_API_KEY
   ```

## Usage

### Download Mods

Downloads mods from NexusMods using their API.

**Usage:**
```bash
dotnet run -- dump [options]
```

**Options:**
- `--list <file>`: Path to file containing mod IDs (one per line)
- `--key <api_key>`: NexusMods API key (optional if using `apikey.txt`)

**What it does:**
1. Reads mod IDs from the specified list file (`data/mod_ids.txt` contains 5,773+ curated mod IDs)
2. Downloads each mod's ZIP file from NexusMods
3. Extracts the ZIP and filters files by extension
4. Saves metadata about each mod
5. Tracks progress to allow resuming interrupted downloads

## Configuration

The application uses `config.json` for configuration:

```json
{
    "StartingModId": 21959,
    "RateLimitDelayMs": 1000,
    "MaxConsecutiveErrors": 10,
    "OutputDirectory": "",
    "ProcessedModsFile": "processed_mods.json",
    "GameId": "cyberpunk2077",
    "DeleteOriginalZip": true,
    "AllowedModFileExtensions": [".zip"],
    "AllowedFileExtensions": [".reds", ".lua", ".json", ".tweak", ".txt", ".md", ".xl", ".wscript", ".xml", ".yaml"],
    "CollectFullMetadata": true,
    "MaxModsToProcess": -1,
    "MinHourlyCallsRemaining": 10,
    "MinDailyCallsRemaining": 50,
    "RateLimitWaitMinutes": 60
}
```

**Key Settings:**
- `AllowedFileExtensions`: File types to keep after extraction
- `RateLimitDelayMs`: Delay between API calls (milliseconds)
- `MaxModsToProcess`: Limit for testing (-1 = unlimited)
- `CollectFullMetadata`: Whether to collect detailed mod information

## File Structure

After running the tool, you'll have:

```
downloaded_mods/              # Downloaded and extracted mods
├── 164/                     # Mod ID folder
│   ├── metadata.json        # Mod metadata
│   ├── SomeModFile/         # Subfolder named after the downloaded file
│   │   └── <extracted files...>  # Actual mod files (filtered by extension)
│   └── SomeModFile.json     # File metadata (same name as subfolder)
├── 447/
│   ├── metadata.json
│   ├── AnotherMod/
│   │   └── <extracted files...>
│   └── AnotherMod.json
└── ...

mod_processing_status.json    # Detailed processing status and tracking
```

## Curated Mod List

The `data/mod_ids.txt` file contains over 5,700 carefully curated Cyberpunk 2077 mod IDs. These mods were selected based on:
- Compatibility with Cyberpunk 2077
- Quality and popularity
- Specific mod types (RedScript, TweakXL, etc.)

## API Rate Limiting

The tool respects NexusMods API limits:
- Configurable delay between requests (default: 1 second)
- Monitors hourly and daily rate limits
- Automatically waits when limits are approached
- Can resume processing after rate limit resets

## Security Notes

- **Never commit `apikey.txt`** - it's in `.gitignore` for safety
- Use `apikey.txt.example` as a template
- API keys are not logged or stored in output files

## Requirements

- .NET 8.0 or later
- Valid NexusMods account with API key
- Internet connection
- Sufficient disk space (some mod collections can be several GB)
