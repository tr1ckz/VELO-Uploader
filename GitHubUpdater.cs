using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace VeloUploader;

public static class GitHubUpdater
{
    private const string RepoOwner = "tr1ckz";
    private const string RepoName = "VELO-Uploader";
    private const string LatestReleaseApi = "https://api.github.com/repos/tr1ckz/VELO-Uploader/releases/latest";

    public record UpdateRelease(Version Version, string TagName, string AssetName, string DownloadUrl, string ReleaseUrl);

    public static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version == null ? new Version(0, 0, 0) : new Version(version.Major, version.Minor, version.Build);
    }

    public static async Task<UpdateRelease?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"VeloUploader/{GetCurrentVersion()}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(LatestReleaseApi, ct);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"Update check failed: Network error - {ex.Message}");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.Warn("Update check: No GitHub releases found. Visit https://github.com/tr1ckz/VELO-Uploader/releases to create releases from tags.");
            }
            else
            {
                Logger.Warn($"Update check failed: GitHub API returned {(int)response.StatusCode}");
            }
            return null;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var releaseUrl = root.TryGetProperty("html_url", out var htmlUrlElement)
                ? (htmlUrlElement.GetString() ?? $"https://github.com/{RepoOwner}/{RepoName}/releases")
                : $"https://github.com/{RepoOwner}/{RepoName}/releases";

            if (!Version.TryParse(tagName.TrimStart('v', 'V'), out var latestVersion))
                return null;

            if (latestVersion <= GetCurrentVersion())
                return null;

            string? assetName = null;
            string? downloadUrl = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                assetName = name;
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }

            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(downloadUrl))
                return null;

            return new UpdateRelease(latestVersion, tagName, assetName, downloadUrl, releaseUrl);
        }
    }

    public static async Task DownloadAndApplyAsync(UpdateRelease release, CancellationToken ct = default, Action<long, long>? onProgress = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "VeloUploaderUpdate", release.TagName + "-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, release.AssetName);
        var extractPath = Path.Combine(tempRoot, "extract");

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"VeloUploader/{GetCurrentVersion()}");
                
                var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                onProgress?.Invoke(0, totalBytes);

                await using var remote = await response.Content.ReadAsStreamAsync(ct);
                await using var local = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                int bytesRead;
                long totalRead = 0;

                while ((bytesRead = await remote.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await local.WriteAsync(buffer, 0, bytesRead, ct);
                    totalRead += bytesRead;
                    onProgress?.Invoke(totalRead, totalBytes);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"Failed to download update: {ex.Message}");
            throw new InvalidOperationException($"Failed to download update from {release.DownloadUrl}: {ex.Message}", ex);
        }

        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VeloUploader.exe");
        var exeName = Path.GetFileName(currentExe);
        var extractedExe = Path.Combine(extractPath, exeName);
        if (!File.Exists(extractedExe))
            throw new InvalidOperationException($"Downloaded update is missing {exeName}.");

        EnsureWritableInstallDirectory(AppContext.BaseDirectory);

        var scriptPath = Path.Combine(tempRoot, "apply-update.cmd");
        var scriptContent = BuildUpdateScript(extractPath, AppContext.BaseDirectory, currentExe, Environment.ProcessId);
        File.WriteAllText(scriptPath, scriptContent, Encoding.ASCII);
        
        Logger.Info($"Update script created at: {scriptPath}");
        Logger.Info($"Source: {extractPath}");
        Logger.Info($"Target: {AppContext.BaseDirectory}");
        Logger.Info($"Exe: {currentExe}");
        Logger.Info($"PID: {Environment.ProcessId}");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = scriptPath,
                WorkingDirectory = tempRoot,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            System.Diagnostics.Process.Start(psi);
            Logger.Info("Update script started successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start update script: {ex.Message}", ex);
            throw;
        }
    }

    private static string BuildUpdateScript(string sourceDir, string targetDir, string exePath, int currentPid)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "VeloUploader_update.log");
        var escapedLogPath = EscapeForCmd(logPath);
        
        return $"@echo off\r\n" +
               "setlocal enabledelayedexpansion\r\n" +
               $"set \"LOGFILE={escapedLogPath}\"\r\n" +
               $"set \"SRC={EscapeForCmd(sourceDir)}\"\r\n" +
               $"set \"DST={EscapeForCmd(targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}\"\r\n" +
               $"set \"EXE={EscapeForCmd(exePath)}\"\r\n" +
               $"set \"PID={currentPid}\"\r\n" +
               ">>\"%LOGFILE%\" echo [%date% %time%] Starting update script\r\n" +
               ">>\"%LOGFILE%\" echo SRC=%SRC%\r\n" +
               ">>\"%LOGFILE%\" echo DST=%DST%\r\n" +
               ">>\"%LOGFILE%\" echo EXE=%EXE%\r\n" +
               ">>\"%LOGFILE%\" echo PID=%PID%\r\n" +
               "REM Check if process is running\r\n" +
               "tasklist /FI \"PID eq !PID!\" 2>nul | find \"!PID!\" >nul\r\n" +
               "if errorlevel 1 >>\"%LOGFILE%\" echo Process already terminated.\r\n" +
               "if not errorlevel 1 >>\"%LOGFILE%\" echo Waiting for process %PID% to exit...\r\n" +
               "REM Wait up to 15 seconds for graceful exit\r\n" +
               "for /l %%i in (1,1,15) do (\r\n" +
               "  tasklist /FI \"PID eq !PID!\" 2>nul | find \"!PID!\" >nul\r\n" +
               "  if errorlevel 1 goto waitdone\r\n" +
               "  timeout /t 1 /nobreak >nul\r\n" +
               ")\r\n" +
               ":waitdone\r\n" +
               "REM Force kill only if still running\r\n" +
               "tasklist /FI \"PID eq !PID!\" 2>nul | find \"!PID!\" >nul\r\n" +
               "if not errorlevel 1 (\r\n" +
               "  >>\"%LOGFILE%\" echo Forcing kill of process %PID%\r\n" +
               "  taskkill /PID %PID% /F 2>>\"%LOGFILE%\" >nul\r\n" +
               "  timeout /t 5 /nobreak >nul\r\n" +
               ")\r\n" +
               ">>\"%LOGFILE%\" echo Starting file replacement...\r\n" +
               "REM Verify directories exist\r\n" +
               "if not exist \"%SRC%\" (\r\n" +
               "  >>\"%LOGFILE%\" echo ERROR: Source directory not found: %SRC%\r\n" +
               "  goto error\r\n" +
               ")\r\n" +
               "if not exist \"%DST%\" (\r\n" +
               "  >>\"%LOGFILE%\" echo ERROR: Target directory not found: %DST%\r\n" +
               "  goto error\r\n" +
               ")\r\n" +
               "REM Clear readonly/system flags from target exe if present\r\n" +
               "if exist \"%EXE%\" attrib -R -H -S \"%EXE%\" 2>>\"%LOGFILE%\"\r\n" +
               "REM Try robocopy multiple times to handle transient file locks\r\n" +
               "set \"RBCODE=16\"\r\n" +
               "for /l %%r in (1,1,5) do (\r\n" +
               "  robocopy \"%SRC%\" \"%DST%\" /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NC /NS /NP /COPY:DAT 2>>\"%LOGFILE%\" >nul\r\n" +
               "  set \"RBCODE=!ERRORLEVEL!\"\r\n" +
               "  if !RBCODE! LSS 8 goto copied\r\n" +
               "  >>\"%LOGFILE%\" echo Robocopy attempt %%r failed with code !RBCODE!\r\n" +
               "  timeout /t 2 /nobreak >nul\r\n" +
               ")\r\n" +
               ">>\"%LOGFILE%\" echo Robocopy failed after retries with code !RBCODE!, trying xcopy fallback...\r\n" +
               "xcopy \"%SRC%\\*\" \"%DST%\\\" /E /I /Y /Q 2>>\"%LOGFILE%\" >nul\r\n" +
               "if errorlevel 1 (\r\n" +
               "  >>\"%LOGFILE%\" echo ERROR: Fallback xcopy failed with code %ERRORLEVEL%\r\n" +
               "  goto error\r\n" +
               ")\r\n" +
               "goto copied\r\n" +
               ":copied\r\n" +
               ">>\"%LOGFILE%\" echo File replacement completed successfully.\r\n" +
               "if exist \"%EXE%\" (\r\n" +
               "  >>\"%LOGFILE%\" echo Restarting application: %EXE%\r\n" +
               "  timeout /t 1 /nobreak >nul\r\n" +
               "  start \"\" \"%EXE%\"\r\n" +
               ") else (\r\n" +
               "  >>\"%LOGFILE%\" echo ERROR: Executable not found: %EXE%\r\n" +
               ")\r\n" +
               ">>\"%LOGFILE%\" echo [%date% %time%] Update script completed.\r\n" +
               "exit /b 0\r\n" +
               ":error\r\n" +
               ">>\"%LOGFILE%\" echo Update failed!\r\n" +
               "exit /b 1\r\n";
        }

    private static void EnsureWritableInstallDirectory(string directory)
    {
        var probe = Path.Combine(directory, ".velo-update-write-test");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("The app folder is not writable. Move VELO Uploader to a writable folder before using auto-update.", ex);
        }
    }

    private static string EscapeForCmd(string value) => value.Replace("\"", "\"\"");

    public static string GetUpdateLogPath() => Path.Combine(Path.GetTempPath(), "VeloUploader_update.log");

    public static string GetUpdateLog()
    {
        var logPath = GetUpdateLogPath();
        try
        {
            if (File.Exists(logPath))
                return File.ReadAllText(logPath);
        }
        catch { }
        return "";
    }
}
