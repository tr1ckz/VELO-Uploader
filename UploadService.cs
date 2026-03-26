using System.Net.Http.Headers;
using System.Text.Json;

namespace VeloUploader;

public class UploadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

    // 50MB chunks for chunked upload
    private const long CHUNK_SIZE = 50 * 1024 * 1024;
    // Files larger than this get chunked upload
    public const long CHUNK_THRESHOLD = 100L * 1024 * 1024;

    public record UploadResult(bool Success, string? Slug, string? Error);

    public static async Task<UploadResult> UploadAsync(
        string serverUrl, string apiToken, string filePath,
        IProgress<double>? progress = null, CancellationToken ct = default,
        int maxRetries = 1, bool preCompressed = false)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists)
        {
            Logger.Error($"File not found: {filePath}");
            return new UploadResult(false, null, "File not found");
        }

        // Use chunked upload for large files
        if (fi.Length > CHUNK_THRESHOLD)
        {
            return await UploadChunkedAsync(serverUrl, apiToken, filePath, fi, progress, ct, maxRetries, preCompressed);
        }

        var fileName = fi.Name;
        var title = Path.GetFileNameWithoutExtension(fileName);

        // Strip .velo-compressed suffix from title
        title = title.Replace(".velo-compressed", "");

        // Try to detect game from parent folder name (ShadowPlay saves to Videos\<GameName>\)
        var parentDir = fi.Directory?.Name;
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (parentDir != null && !string.Equals(fi.DirectoryName, videosDir, StringComparison.OrdinalIgnoreCase))
        {
            title = $"{parentDir} - {title}";
        }

        var url = serverUrl.TrimEnd('/') + "/api/videos";

        Logger.Info($"Uploading: {fileName} ({fi.Length / 1024 / 1024}MB) → {url}");
        Logger.Debug($"Title: {title}");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (attempt > 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * 5); // 5s, 10s, 20s...
                Logger.Info($"Retry {attempt}/{maxRetries} in {delay.TotalSeconds}s...");
                await Task.Delay(delay, ct);
            }

            var result = await TryUploadOnce(url, apiToken, filePath, fi, fileName, title, progress, ct, preCompressed);

            if (result.Success)
                return result;

            if (attempt < maxRetries)
                Logger.Warn($"Upload attempt {attempt} failed: {result.Error}");
            else
                return result;
        }

        return new UploadResult(false, null, "Max retries exceeded");
    }

    private static async Task<UploadResult> TryUploadOnce(
        string url, string apiToken, string filePath, FileInfo fi,
        string fileName, string title,
        IProgress<double>? progress, CancellationToken ct,
        bool preCompressed = false)
    {
        var mimeType = fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4"
            : fileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ? "video/x-matroska"
            : fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm"
            : fileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime"
            : fileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ? "video/x-msvideo"
            : "video/mp4";

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var progressStream = new ProgressStream(fileStream, fi.Length, progress);
            using var content = new StreamContent(progressStream, 81920);

            content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            content.Headers.ContentLength = fi.Length;

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.Add("X-Upload-Filename", Uri.EscapeDataString(fileName));
            request.Headers.Add("X-Upload-Title", Uri.EscapeDataString(title));
            if (preCompressed)
                request.Headers.Add("X-Pre-Compressed", "true");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var slug = doc.RootElement.GetProperty("slug").GetString();
                Logger.Info($"Upload complete: {fileName} → /v/{slug}");
                return new UploadResult(true, slug, null);
            }

            // Try to extract error message
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var error = doc.RootElement.GetProperty("error").GetString();
                Logger.Error($"Upload failed ({(int)response.StatusCode}): {error ?? body}");
                return new UploadResult(false, null, error ?? $"HTTP {(int)response.StatusCode}");
            }
            catch
            {
                Logger.Error($"Upload failed ({(int)response.StatusCode}): {body}");
                return new UploadResult(false, null, $"HTTP {(int)response.StatusCode}: {body}");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warn($"Upload cancelled: {fileName}");
            return new UploadResult(false, null, "Upload cancelled");
        }
        catch (Exception ex)
        {
            Logger.Error($"Upload error: {fileName}", ex);
            return new UploadResult(false, null, ex.Message);
        }
    }
    /// <summary>
    /// Upload a large file in chunks via POST /api/upload/chunked
    /// Flow: init → upload chunks → complete
    /// </summary>
    private static async Task<UploadResult> UploadChunkedAsync(
        string serverUrl, string apiToken, string filePath, FileInfo fi,
        IProgress<double>? progress, CancellationToken ct,
        int maxRetries, bool preCompressed)
    {
        var fileName = fi.Name;
        var title = Path.GetFileNameWithoutExtension(fileName).Replace(".velo-compressed", "");

        var parentDir = fi.Directory?.Name;
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (parentDir != null && !string.Equals(fi.DirectoryName, videosDir, StringComparison.OrdinalIgnoreCase))
            title = $"{parentDir} - {title}";

        var mimeType = fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4"
            : fileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ? "video/x-matroska"
            : fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm"
            : fileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime"
            : fileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ? "video/x-msvideo"
            : "video/mp4";

        var baseUrl = serverUrl.TrimEnd('/') + "/api/upload/chunked";
        var totalChunks = (int)Math.Ceiling((double)fi.Length / CHUNK_SIZE);
        var sessionKey = UploadResumeStore.BuildKey(serverUrl, filePath, fi, preCompressed);

        Logger.Info($"Chunked upload: {fileName} ({fi.Length / 1024 / 1024}MB, {totalChunks} chunks)");

        // Step 1: Init chunked upload
        string uploadId;
        HashSet<int> receivedChunks = [];
        try
        {
            var existing = UploadResumeStore.Get(sessionKey);
            if (existing != null)
            {
                var status = await GetChunkedUploadStatusAsync(baseUrl, apiToken, existing.UploadId, ct);
                if (status != null)
                {
                    uploadId = existing.UploadId;
                    receivedChunks = status;
                    Logger.Info($"Resuming previous upload: {fileName} ({receivedChunks.Count}/{totalChunks} chunks already present)");
                }
                else
                {
                    uploadId = await InitializeChunkedUploadAsync(baseUrl, apiToken, fileName, title, mimeType, fi.Length, totalChunks, preCompressed, ct);
                    if (string.IsNullOrWhiteSpace(uploadId))
                        return new UploadResult(false, null, "Chunk init failed");
                    UploadResumeStore.Save(new UploadResumeSession
                    {
                        Key = sessionKey,
                        UploadId = uploadId,
                        FilePath = filePath,
                        ServerUrl = serverUrl,
                        FileSize = fi.Length,
                        LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks,
                        PreCompressed = preCompressed,
                    });
                }
            }
            else
            {
                uploadId = await InitializeChunkedUploadAsync(baseUrl, apiToken, fileName, title, mimeType, fi.Length, totalChunks, preCompressed, ct);
                if (string.IsNullOrWhiteSpace(uploadId))
                    return new UploadResult(false, null, "Chunk init failed");
                UploadResumeStore.Save(new UploadResumeSession
                {
                    Key = sessionKey,
                    UploadId = uploadId,
                    FilePath = filePath,
                    ServerUrl = serverUrl,
                    FileSize = fi.Length,
                    LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks,
                    PreCompressed = preCompressed,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Chunk init error", ex);
            return new UploadResult(false, null, $"Chunk init error: {ex.Message}");
        }

        // Step 2: Upload each chunk
        var buffer = new byte[CHUNK_SIZE];
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        long totalBytesUploaded = receivedChunks.Sum(index => GetChunkLength(index, fi.Length, totalChunks));
        if (totalBytesUploaded > 0)
            progress?.Report((double)totalBytesUploaded / fi.Length * 100);

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var bytesToRead = (int)GetChunkLength(chunkIndex, fi.Length, totalChunks);
            if (receivedChunks.Contains(chunkIndex))
            {
                fileStream.Seek(bytesToRead, SeekOrigin.Current);
                continue;
            }

            var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            if (bytesRead == 0) break;

            bool chunkSuccess = false;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var chunkReq = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}/{uploadId}/{chunkIndex}");
                    chunkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
                    chunkReq.Content = new ByteArrayContent(buffer, 0, bytesRead);
                    chunkReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    using var chunkResp = await Http.SendAsync(chunkReq, ct);
                    if (chunkResp.IsSuccessStatusCode)
                    {
                        totalBytesUploaded += bytesRead;
                        receivedChunks.Add(chunkIndex);
                        progress?.Report((double)totalBytesUploaded / fi.Length * 100);
                        chunkSuccess = true;
                        break;
                    }

                    var errText = await chunkResp.Content.ReadAsStringAsync(ct);
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} failed: HTTP {(int)chunkResp.StatusCode} - {errText}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} error: {ex.Message}");
                }

                if (attempt < maxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }

            if (!chunkSuccess)
            {
                Logger.Error($"Chunk {chunkIndex}/{totalChunks} failed after {maxRetries} attempts");
                return new UploadResult(false, null, $"Failed uploading chunk {chunkIndex + 1}/{totalChunks}");
            }
        }

        // Step 3: Complete the upload
        try
        {
            using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{uploadId}/complete");
            completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var completeResp = await Http.SendAsync(completeReq, ct);
            var completeText = await completeResp.Content.ReadAsStringAsync(ct);

            if (completeResp.IsSuccessStatusCode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(completeText);
                var slug = doc.RootElement.GetProperty("slug").GetString();
                Logger.Info($"Chunked upload complete: {fileName} → /v/{slug}");
                UploadResumeStore.Remove(sessionKey);
                return new UploadResult(true, slug, null);
            }

            Logger.Error($"Chunk complete failed ({(int)completeResp.StatusCode}): {completeText}");
            return new UploadResult(false, null, $"Complete failed: HTTP {(int)completeResp.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.Error("Chunk complete error", ex);
            return new UploadResult(false, null, $"Complete error: {ex.Message}");
        }
    }

    private static async Task<string> InitializeChunkedUploadAsync(
        string baseUrl,
        string apiToken,
        string fileName,
        string title,
        string mimeType,
        long totalSize,
        int totalChunks,
        bool preCompressed,
        CancellationToken ct)
    {
        using var initReq = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var initBody = JsonSerializer.Serialize(new
        {
            filename = fileName,
            title,
            mimeType,
            totalSize,
            totalChunks,
            preCompressed,
        });
        initReq.Content = new StringContent(initBody, System.Text.Encoding.UTF8, "application/json");
        using var initResp = await Http.SendAsync(initReq, ct);
        var initText = await initResp.Content.ReadAsStringAsync(ct);

        if (!initResp.IsSuccessStatusCode)
        {
            Logger.Error($"Chunk init failed ({(int)initResp.StatusCode}): {initText}");
            return "";
        }

        using var doc = JsonDocument.Parse(initText);
        var uploadId = doc.RootElement.GetProperty("uploadId").GetString() ?? "";
        Logger.Debug($"Chunked upload initialized: {uploadId}");
        return uploadId;
    }

    private static async Task<HashSet<int>?> GetChunkedUploadStatusAsync(
        string baseUrl,
        string apiToken,
        string uploadId,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{uploadId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var received = new HashSet<int>();
            foreach (var chunk in doc.RootElement.GetProperty("receivedChunks").EnumerateArray())
            {
                received.Add(chunk.GetInt32());
            }
            return received;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not resume previous chunked upload {uploadId}: {ex.Message}");
            return null;
        }
    }

    private static long GetChunkLength(int chunkIndex, long totalSize, int totalChunks)
    {
        if (chunkIndex < totalChunks - 1)
            return CHUNK_SIZE;
        var lastChunkSize = totalSize - (CHUNK_SIZE * (totalChunks - 1));
        return lastChunkSize > 0 ? lastChunkSize : CHUNK_SIZE;
    }
}

/// <summary>
/// Wraps a stream to report read progress.
/// </summary>
internal class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly long _totalLength;
    private readonly IProgress<double>? _progress;
    private long _bytesRead;

    public ProgressStream(Stream inner, long totalLength, IProgress<double>? progress)
    {
        _inner = inner;
        _totalLength = totalLength;
        _progress = progress;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _inner.Read(buffer, offset, count);
        _bytesRead += n;
        _progress?.Report(_totalLength > 0 ? (double)_bytesRead / _totalLength * 100 : 0);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var n = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct);
        _bytesRead += n;
        _progress?.Report(_totalLength > 0 ? (double)_bytesRead / _totalLength * 100 : 0);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var n = await _inner.ReadAsync(buffer, ct);
        _bytesRead += n;
        _progress?.Report(_totalLength > 0 ? (double)_bytesRead / _totalLength * 100 : 0);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
