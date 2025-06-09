# NexusDump - Cyberpunk 2077 Mod Downloader

A C# console application that downloads and processes Cyberpunk 2077 mods from NexusMods using their API.

## Features

- **Authentication**: Uses NexusMods API key for authentication
- **Progressive Download**: Starts from mod ID 21959 and works backwards
- **Resume Support**: Saves processed mods to avoid re-downloading
- **Rate Limiting**: Respects API rate limits with 1-second delays between requests
- **File Processing**:
  - Downloads the first file of each mod
  - Extracts ZIP files
  - Removes large `.archive` files to save space
  - Creates metadata files with mod information
- **Error Handling**: Continues processing even if individual mods fail

## Setup

1. **Get your NexusMods API Key**:
   - Go to [NexusMods](https://www.nexusmods.com)
   - Log into your account
   - Go to your profile settings
   - Generate an API key under the "API Keys" section

2. **Configure API Key** (Optional but recommended):
   - Create a file named `apikey.txt` in the project directory
   - Paste your API key into this file (one line, no extra spaces)
   - The application will automatically use this key
   - If no file exists, you'll be prompted to enter the key manually

3. **Build the project**:

   ```powershell
   dotnet build
   ```

## Usage

1. **Run the application**:

   ```powershell
   dotnet run
   ```

2. **Enter your API key** when prompted

3. **The application will**:
   - Start downloading from mod ID 21959
   - Create a `downloaded_mods` folder
   - For each mod, create a subfolder named with the mod ID
   - Extract the mod files (excluding .archive files)
   - Create a `metadata.json` file with mod information
   - Track processed mods in `processed_mods.json`

## Output Structure

```txt
downloaded_mods/
├── 21959/
│   ├── extracted/          # Extracted mod files (no .archive files)
│   └── metadata.json       # Mod metadata
├── 21958/
│   ├── extracted/
│   └── metadata.json
└── ...
processed_mods.json         # List of processed mod IDs
```

## Metadata Format

Each `metadata.json` contains:

- Mod ID, name, summary, description
- Author information
- Version information
- Download and endorsement counts
- Tags
- File information (name, version, size)

## Rate Limiting

The application includes a 1-second delay between API requests to respect NexusMods' rate limiting. This means processing will be slow but steady.

## Error Handling

- Skips mods that are not accessible or don't exist
- Continues processing if individual downloads fail
- Stops after 10 consecutive errors to prevent infinite loops
- Saves progress regularly so you can resume if interrupted

## Resume Functionality

If the application is interrupted, simply run it again. It will:

- Load the list of already processed mods from `processed_mods.json`
- Skip mods that have already been processed
- Continue from where it left off

## Configuration

You can customize the application behavior by editing `config.json`:

```json
{
    "StartingModId": 21959,           // Latest mod ID to start from
    "RateLimitDelayMs": 1000,         // Delay between API calls in milliseconds
    "MaxConsecutiveErrors": 10,       // Stop after this many consecutive errors
    "OutputDirectory": "downloaded_mods", // Where to save downloaded mods
    "ProcessedModsFile": "processed_mods.json", // Track processed mods
    "GameId": "cyberpunk2077",        // NexusMods game identifier
    "DeleteArchiveFiles": true,       // Remove large .archive files after extraction
    "DeleteOriginalZip": true,        // Remove ZIP file after extraction
    "MaxModsToProcess": 10            // Debug: limit number of mods (-1 = unlimited)
}
```

**Debug Mode**: Set `MaxModsToProcess` to a positive number (e.g., 10) to process only that many mods for testing. Set to -1 for unlimited processing.

## Notes

- The application downloads only the first file of each mod
- Large `.archive` files are automatically deleted to save disk space
- All mod files are extracted from ZIP archives
- The original ZIP files are deleted after extraction to save space

## Security

- **API Key File**: The `apikey.txt` file is automatically excluded from version control via `.gitignore`
- **Example File**: Use `apikey.txt.example` as a template - copy it to `apikey.txt` and add your real API key
- **Manual Input**: If no `apikey.txt` file exists, you'll be prompted to enter the key manually each time

## Requirements

- .NET 9.0
- Valid NexusMods account with API key
- Internet connection
- Sufficient disk space for mod files
