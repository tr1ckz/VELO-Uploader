# VELO Uploader

A Windows system tray application that watches for new NVIDIA Instant Replay (ShadowPlay) clips and automatically uploads them to your [VELO](https://github.com/tr1ckz/VELO) instance.

## Features

- **Auto-detect new clips** — Watches your ShadowPlay save folder using `FileSystemWatcher`
- **Local compression** — Optionally compresses clips with FFmpeg before uploading to save bandwidth and storage
- **Duplicate detection** — Computes a SHA-256 hash before each upload and skips files already in your history
- **Chunked streaming upload** — Large files upload in chunks without loading into memory; interrupted uploads resume automatically
- **Game detection** — Reads the game name from ShadowPlay's folder structure and tags the video on VELO
- **Webhook notifications** — Posts the clip URL to a Discord, Stoat.chat, or compatible webhook after each upload
- **Upload history** — Recent uploads stored locally with timestamp, URL, and file hash
- **Desktop notifications** — Toast notifications for upload success, errors, and skipped duplicates
- **Single-file executable** — No installer needed, just run `VeloUploader.exe`

## Setup

1. Download the latest release from [Releases](../../releases)
2. Run `VeloUploader.exe`
3. On first launch the settings window opens:
   - **Server URL** — Your VELO instance URL (e.g. `https://clips.example.com`)
   - **API Token** — Generate one in VELO → Dashboard → Settings → API Tokens
   - **Watch Folder** — Your ShadowPlay save directory (usually `C:\Users\<you>\Videos`)
   - **Webhook URL** *(optional)* — Discord or Stoat.chat webhook to receive clip links
   - **Compress before upload** *(optional)* — Enables local FFmpeg compression
4. Click **Test** to verify the connection
5. Click **Save & Start Watching**

The app runs in the system tray. Double-click the tray icon to open settings. Right-click for pause / resume / exit.

## How it works

1. NVIDIA ShadowPlay saves a clip to `Videos\<GameName>\clip.mp4`
2. `FileSystemWatcher` detects the new file and waits for ShadowPlay to finish writing
3. A SHA-256 hash is computed — if the file matches a previous upload it is skipped
4. If compression is enabled, FFmpeg re-encodes the clip and the original is deleted
5. The file streams to VELO's chunked upload API with a Bearer token
6. On success, a toast shows the clip URL and optionally posts it to the configured webhook

## Building from source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

FFmpeg is optional but recommended for the compression feature. The app works fine without it — compression will simply be unavailable.

```powershell
dotnet build
dotnet run
```

### FFmpeg Setup

**Option 1: Portable FFmpeg (bundled in releases)** ⭐ *Recommended for users*
- Download the latest release from [Releases](../../releases)
- The `.zip` file includes a portable FFmpeg build
- Just extract and run — no additional installation needed

**Option 2: Install via winget** *For source builds and development*
```powershell
winget install -e --id Gyan.FFmpeg
```

**Option 3: Download standalone** *Manual setup*
- Download from [FFmpeg official](https://ffmpeg.org/download.html) or [gyan.dev](https://www.gyan.dev/ffmpeg/)
- Extract to `ffmpeg-portable` folder inside the app directory, or add to your system PATH

### Self-contained release build

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

The GitHub Actions workflow in `.github/workflows/release.yml` runs this automatically when a version tag is pushed, and bundles portable FFmpeg in the release .zip.


