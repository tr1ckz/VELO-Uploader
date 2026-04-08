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

    private static int ScoreAssetName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith("-win-x64.zip")) return 0;
        if (lower.Contains("win-x64") && lower.EndsWith(".zip")) return 1;
        if (lower.EndsWith(".zip")) return 2;
        if (lower.EndsWith(".exe")) return 3;
        return 10;
    }

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
            throw new InvalidOperationException($"Could not contact GitHub to check for updates: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.Warn("Update check: No GitHub releases found. Visit https://github.com/tr1ckz/VELO-Uploader/releases to create releases from tags.");
                throw new InvalidOperationException("No published GitHub release was found yet.");
            }

            Logger.Warn($"Update check failed: GitHub API returned {(int)response.StatusCode}");
            throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode} while checking for updates.");
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

            var asset = root.GetProperty("assets")
                .EnumerateArray()
                .Select(item => new
                {
                    Name = item.GetProperty("name").GetString() ?? string.Empty,
                    Url = item.GetProperty("browser_download_url").GetString() ?? string.Empty,
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Url))
                .OrderBy(item => ScoreAssetName(item.Name))
                .FirstOrDefault(item => ScoreAssetName(item.Name) < 10);

            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"A newer version ({tagName}) exists, but its Windows package is not attached yet. Try again in a minute or open {releaseUrl}.");
            }

            return new UpdateRelease(latestVersion, tagName, asset.Name, asset.Url, releaseUrl);
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
        var extractedExe = Directory
            .EnumerateFiles(extractPath, exeName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(extractedExe) || !File.Exists(extractedExe))
            throw new InvalidOperationException($"Downloaded update is missing {exeName}.");

        var extractedRoot = Path.GetDirectoryName(extractedExe) ?? extractPath;

        EnsureWritableInstallDirectory(AppContext.BaseDirectory);

        var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
        var scriptContent = BuildUpdateScript(extractedRoot, AppContext.BaseDirectory, currentExe, Environment.ProcessId);
        File.WriteAllText(scriptPath, scriptContent, Encoding.UTF8);
        
        Logger.Info($"Update script created at: {scriptPath}");
        Logger.Info($"Source: {extractPath}");
        Logger.Info($"Target: {AppContext.BaseDirectory}");
        Logger.Info($"Exe: {currentExe}");
        Logger.Info($"PID: {Environment.ProcessId}");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = true,
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
        var exeName = Path.GetFileName(exePath);
        var dst = targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Embed values as single-quoted PS literals (escape single quotes by doubling them)
        static string Ps(string s) => "'" + s.Replace("'", "''") + "'";

        return
            $"$LOGFILE = {Ps(logPath)}\r\n" +
            $"$SRC     = {Ps(sourceDir)}\r\n" +
            $"$DST     = {Ps(dst)}\r\n" +
            $"$EXE     = {Ps(exePath)}\r\n" +
            $"$EXENAME = {Ps(exeName)}\r\n" +
            $"$WAITPID = {currentPid}\r\n" +
            "\r\n" +
            "function Log([string]$msg) { \"$(Get-Date -Format 'o') $msg\" | Add-Content -LiteralPath $LOGFILE }\r\n" +
            "\r\n" +
            "Log 'Starting update script'\r\n" +
            "Log \"SRC=$SRC\"\r\n" +
            "Log \"DST=$DST\"\r\n" +
            "Log \"EXE=$EXE\"\r\n" +
            "Log \"PID=$WAITPID\"\r\n" +
            "\r\n" +
            "# Wait for old process to exit (up to 30s)\r\n" +
            "$proc = Get-Process -Id $WAITPID -ErrorAction SilentlyContinue\r\n" +
            "if ($proc) {\r\n" +
            "    Log 'Waiting for process to exit...'\r\n" +
            "    $waited = 0\r\n" +
            "    while ($waited -lt 30) {\r\n" +
            "        Start-Sleep -Seconds 1\r\n" +
            "        $waited++\r\n" +
            "        if (-not (Get-Process -Id $WAITPID -ErrorAction SilentlyContinue)) { break }\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "# Force kill if still alive\r\n" +
            "if (Get-Process -Id $WAITPID -ErrorAction SilentlyContinue) {\r\n" +
            "    Log 'Force killing process'\r\n" +
            "    Stop-Process -Id $WAITPID -Force -ErrorAction SilentlyContinue\r\n" +
            "    Start-Sleep -Seconds 2\r\n" +
            "}\r\n" +
            "\r\n" +
            "Log 'Starting file replacement...'\r\n" +
            "\r\n" +
            "if (-not (Test-Path -LiteralPath $SRC)) { Log \"ERROR: SRC not found: $SRC\"; Start-Process $EXE; exit 1 }\r\n" +
            "if (-not (Test-Path -LiteralPath $DST)) { Log \"ERROR: DST not found: $DST\"; Start-Process $EXE; exit 1 }\r\n" +
            "\r\n" +
            "# Copy all non-exe files\r\n" +
            "try {\r\n" +
            "    Get-ChildItem -LiteralPath $SRC -Recurse -File | Where-Object { $_.Name -ne $EXENAME } | ForEach-Object {\r\n" +
            "        $rel  = $_.FullName.Substring($SRC.Length).TrimStart([char]'\\', [char]'/')\r\n" +
            "        $dest = Join-Path $DST $rel\r\n" +
            "        $dir  = Split-Path $dest -Parent\r\n" +
            "        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }\r\n" +
            "        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force\r\n" +
            "    }\r\n" +
            "    Log 'Non-exe files copied.'\r\n" +
            "} catch {\r\n" +
            "    Log \"WARNING: Non-exe copy error: $_\"\r\n" +
            "}\r\n" +
            "\r\n" +
            "# Replace exe with retries\r\n" +
            "$srcExe = Join-Path $SRC $EXENAME\r\n" +
            "if (-not (Test-Path -LiteralPath $srcExe)) { Log \"ERROR: Source exe not found: $srcExe\"; Start-Process $EXE; exit 1 }\r\n" +
            "$replaced = $false\r\n" +
            "for ($i = 1; $i -le 20; $i++) {\r\n" +
            "    try {\r\n" +
            "        Copy-Item -LiteralPath $srcExe -Destination $EXE -Force -ErrorAction Stop\r\n" +
            "        $replaced = $true\r\n" +
            "        Log \"Exe replaced on attempt $i\"\r\n" +
            "        break\r\n" +
            "    } catch {\r\n" +
            "        Log \"Exe copy attempt $i failed: $_\"\r\n" +
            "        Start-Sleep -Seconds 1\r\n" +
            "    }\r\n" +
            "}\r\n" +
            "\r\n" +
            "if (-not $replaced) { Log 'ERROR: Failed to replace exe after 20 attempts.'; Start-Process $EXE; exit 1 }\r\n" +
            "\r\n" +
            "Log 'Update complete. Restarting...'\r\n" +
            "Start-Sleep -Seconds 1\r\n" +
            "Start-Process -FilePath $EXE\r\n" +
            "Log 'Done.'\r\n" +
            "exit 0\r\n";
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
