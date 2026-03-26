# VELO Uploader

A Windows system tray application that watches for new NVIDIA Instant Replay (ShadowPlay) clips and automatically uploads them to your [VELO](https://github.com/tr1ckz/VELO) instance.

## Features

- **Auto-detect new clips** — Watches your ShadowPlay save folder using FileSystemWatcher
- **Streaming upload** — Files stream directly to the server without loading into memory
- **Game detection** — Automatically detects game name from ShadowPlay's folder structure
- **Progress tracking** — Upload progress shown in the system tray tooltip
- **Desktop notifications** — Toast notifications for new clips and upload status
- **Single-file executable** — No installer needed, just run the `.exe`

## Setup

1. Download the latest release from [Releases](../../releases)
2. Run `VeloUploader.exe`
3. On first launch, the settings window opens:
   - **Server URL** — Your VELO instance URL (e.g. `https://clips.example.com`)
   - **API Token** — Generate one from VELO → Dashboard → Settings → API Tokens
   - **Watch Folder** — Your ShadowPlay save directory (usually `C:\Users\<you>\Videos`)
4. Click **Test** to verify the connection
5. Click **Save & Start Watching**

The app runs in the system tray. Double-click the tray icon to open settings. Right-click for pause/resume/exit.

## Building from source

Requires [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
dotnet build
dotnet run
```

### Self-contained release build

```powershell
dotnet publish -c Release -r win-x64 --self-contained -o publish
```

## How it works

1. NVIDIA ShadowPlay saves a clip to `Videos\<GameName>\clip.mp4`
2. FileSystemWatcher detects the new file
3. Waits for ShadowPlay to finish writing (retries exclusive file access)
4. Streams the file to VELO's `/api/videos` endpoint with a Bearer token
5. Shows a desktop notification with the clip URL on success
