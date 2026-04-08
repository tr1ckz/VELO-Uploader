using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace VeloUploader;

public class UploadService
{
    private static HttpClient _http = BuildClient(null);

    /// <summary>
    /// Rebuild the shared <see cref="HttpClient"/> using TLS settings from <paramref name="settings"/>.
    /// Call this whenever TLS-related settings change (on startup and after each settings save).
    /// </summary>
    public static void Reconfigure(AppSettings settings)
    {
        var old = _http;
        _http = BuildClient(settings);
        // Dispose the old client after a delay to let any in-flight requests complete.
        Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => old.Dispose());
    }

    private static HttpClient BuildClient(AppSettings? settings)
    {
        var handler = settings != null
            ? TlsCertHelper.CreateHandler(settings)
            : new HttpClientHandler();
        return new HttpClient(handler) { Timeout = TimeSpan.FromHours(1) };
    }

    // 50MB chunks for chunked upload
    private const long CHUNK_SIZE = 50 * 1024 * 1024;
    // Files larger than this get chunked upload
    public const long CHUNK_THRESHOLD = 100L * 1024 * 1024;

    public record UploadResult(
        bool Success,
        string? Slug,
        string? Error,
        string? TraceId = null,
        bool Duplicate = false,
        bool Retryable = false,
        TimeSpan? RetryAfter = null);

    private sealed record ChunkInitResult(string UploadId, string? Error = null, bool Retryable = false, TimeSpan? RetryAfter = null);

    public static async Task<UploadResult> UploadAsync(
        string serverUrl, string apiToken, string filePath,
        IProgress<double>? progress = null, CancellationToken ct = default,
        int maxRetries = 1, bool preCompressed = false, bool requireChecksum = false)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists)
        {
            Logger.Error($"File not found: {filePath}");
            return new UploadResult(false, null, "File not found");
        }

        // Use chunked upload for large files
        var checksum = await ComputeSha256Async(filePath, ct);
        var traceId = Guid.NewGuid().ToString("N");
        if (requireChecksum && string.IsNullOrWhiteSpace(checksum))
        {
            Logger.Error($"Checksum required but unavailable for {Path.GetFileName(filePath)}");
            return new UploadResult(false, null, "Checksum required but could not be computed", traceId);
        }

        if (fi.Length > CHUNK_THRESHOLD)
        {
            return await UploadChunkedAsync(serverUrl, apiToken, filePath, fi, progress, ct, maxRetries, preCompressed, checksum, traceId);
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

        Logger.Info($"Uploading: {fileName} ({fi.Length / 1024 / 1024}MB) â†’ {url}");
        Logger.Debug($"Title: {title}");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (attempt > 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * 5); // 5s, 10s, 20s...
                Logger.Info($"Retry {attempt}/{maxRetries} in {delay.TotalSeconds}s...");
                await Task.Delay(delay, ct);
            }

            var result = await TryUploadOnce(url, apiToken, filePath, fi, fileName, title, progress, ct, preCompressed, checksum, traceId);

            if (result.Success)
                return result;

            if (attempt < maxRetries)
                Logger.Warn($"Upload attempt {attempt} failed: {result.Error}");
            else
                return result;
        }

        return new UploadResult(false, null, "Max retries exceeded", traceId);
    }

    private static async Task<UploadResult> TryUploadOnce(
        string url, string apiToken, string filePath, FileInfo fi,
        string fileName, string title,
        IProgress<double>? progress, CancellationToken ct,
        bool preCompressed = false,
        string? checksum = null,
        string? traceId = null)
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
            if (!string.IsNullOrWhiteSpace(checksum))
                request.Headers.Add("X-Upload-SHA256", checksum);
            if (!string.IsNullOrWhiteSpace(traceId))
                request.Headers.Add("X-Trace-Id", traceId);
            if (preCompressed)
                request.Headers.Add("X-Pre-Compressed", "true");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("duplicate", out var dup) && dup.GetBoolean())
                {
                    var dupSlug = doc.RootElement.GetProperty("duplicateOf").GetProperty("slug").GetString();
                    var resTrace = doc.RootElement.TryGetProperty("traceId", out var dupTrace) ? dupTrace.GetString() : traceId;
                    Logger.Info($"Duplicate detected: {fileName} â†’ /v/{dupSlug}");
                    return new UploadResult(true, dupSlug, null, resTrace, true);
                }

                var slug = doc.RootElement.GetProperty("slug").GetString();
                var responseTrace = doc.RootElement.TryGetProperty("traceId", out var traceEl) ? traceEl.GetString() : traceId;
                Logger.Info($"Upload complete: {fileName} â†’ /v/{slug} (trace={responseTrace})");
                return new UploadResult(true, slug, null, responseTrace);
            }

            // Try to extract error message
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var error = doc.RootElement.GetProperty("error").GetString();
                Logger.Error($"Upload failed ({(int)response.StatusCode}): {error ?? body}");
                return new UploadResult(
                    false,
                    null,
                    error ?? $"HTTP {(int)response.StatusCode}",
                    traceId,
                    false,
                    IsTransientStatusCode(response.StatusCode),
                    GetRetryAfter(response));
            }
            catch
            {
                Logger.Error($"Upload failed ({(int)response.StatusCode}): {body}");
                return new UploadResult(
                    false,
                    null,
                    $"HTTP {(int)response.StatusCode}: {body}",
                    traceId,
                    false,
                    IsTransientStatusCode(response.StatusCode),
                    GetRetryAfter(response));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warn($"Upload timed out: {fileName}");
            return new UploadResult(false, null, "Upload timed out", traceId, false, true);
        }
        catch (OperationCanceledException)
        {
            Logger.Warn($"Upload cancelled: {fileName}");
            return new UploadResult(false, null, "Upload cancelled", traceId);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"Upload network error: {fileName}", ex);
            return new UploadResult(false, null, ex.Message, traceId, false, true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Upload error: {fileName}", ex);
            return new UploadResult(false, null, ex.Message, traceId);
        }
    }
    /// <summary>
    /// Upload a large file in chunks via POST /api/upload/chunked
    /// Flow: init â†’ upload chunks â†’ complete
    /// </summary>
    private static async Task<UploadResult> UploadChunkedAsync(
        string serverUrl, string apiToken, string filePath, FileInfo fi,
        IProgress<double>? progress, CancellationToken ct,
        int maxRetries, bool preCompressed,
        string? checksum,
        string? traceId)
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
                    var initResult = await InitializeChunkedUploadAsync(baseUrl, apiToken, fileName, title, mimeType, fi.Length, totalChunks, preCompressed, checksum, traceId, ct);
                    if (string.IsNullOrWhiteSpace(initResult.UploadId))
                        return new UploadResult(false, null, initResult.Error ?? "Chunk init failed", traceId, false, initResult.Retryable, initResult.RetryAfter);
                    uploadId = initResult.UploadId;
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
                var initResult = await InitializeChunkedUploadAsync(baseUrl, apiToken, fileName, title, mimeType, fi.Length, totalChunks, preCompressed, checksum, traceId, ct);
                if (string.IsNullOrWhiteSpace(initResult.UploadId))
                    return new UploadResult(false, null, initResult.Error ?? "Chunk init failed", traceId, false, initResult.Retryable, initResult.RetryAfter);
                uploadId = initResult.UploadId;
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
            var retryable = ex is HttpRequestException || ex is TaskCanceledException;
            return new UploadResult(false, null, $"Chunk init error: {ex.Message}", traceId, false, retryable);
        }

        // Step 2: Upload each chunk
        var buffer = new byte[CHUNK_SIZE];
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        long totalBytesUploaded = receivedChunks.Sum(index => GetChunkLength(index, fi.Length, totalChunks));
        if (totalBytesUploaded > 0)
            progress?.Report((double)totalBytesUploaded / fi.Length * 100);

        HttpStatusCode? lastChunkStatus = null;
        TimeSpan? lastChunkRetryAfter = null;
        Exception? lastChunkException = null;

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

                    using var chunkResp = await _http.SendAsync(chunkReq, ct);
                    if (chunkResp.IsSuccessStatusCode)
                    {
                        totalBytesUploaded += bytesRead;
                        receivedChunks.Add(chunkIndex);
                        progress?.Report((double)totalBytesUploaded / fi.Length * 100);
                        chunkSuccess = true;
                        lastChunkStatus = null;
                        lastChunkRetryAfter = null;
                        lastChunkException = null;
                        break;
                    }

                    lastChunkStatus = chunkResp.StatusCode;
                    lastChunkRetryAfter = GetRetryAfter(chunkResp);
                    var errText = await chunkResp.Content.ReadAsStringAsync(ct);
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} failed: HTTP {(int)chunkResp.StatusCode} - {errText}");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    lastChunkException = new TimeoutException($"Chunk {chunkIndex} timed out");
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} timed out");
                }
                catch (HttpRequestException ex)
                {
                    lastChunkException = ex;
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} network error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    lastChunkException = ex;
                    Logger.Warn($"Chunk {chunkIndex} attempt {attempt} error: {ex.Message}");
                }

                if (attempt < maxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }

            if (!chunkSuccess)
            {
                Logger.Error($"Chunk {chunkIndex}/{totalChunks} failed after {maxRetries} attempts");
                var retryable = lastChunkException is HttpRequestException or TimeoutException
                    || (lastChunkStatus.HasValue && IsTransientStatusCode(lastChunkStatus.Value));
                return new UploadResult(false, null, $"Failed uploading chunk {chunkIndex + 1}/{totalChunks}", traceId, false, retryable, lastChunkRetryAfter);
            }
        }

        // Step 3: Complete the upload
        try
        {
            using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{uploadId}/complete");
            completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var completeResp = await _http.SendAsync(completeReq, ct);
            var completeText = await completeResp.Content.ReadAsStringAsync(ct);

            if (completeResp.IsSuccessStatusCode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(completeText);
                if (doc.RootElement.TryGetProperty("duplicate", out var dup) && dup.GetBoolean())
                {
                    var dupSlug = doc.RootElement.GetProperty("duplicateOf").GetProperty("slug").GetString();
                    var dupTrace = doc.RootElement.TryGetProperty("traceId", out var t) ? t.GetString() : traceId;
                    Logger.Info($"Chunked duplicate detected: {fileName} â†’ /v/{dupSlug}");
                    UploadResumeStore.Remove(sessionKey);
                    return new UploadResult(true, dupSlug, null, dupTrace, true);
                }
                var slug = doc.RootElement.GetProperty("slug").GetString();
                var responseTrace = doc.RootElement.TryGetProperty("traceId", out var t2) ? t2.GetString() : traceId;
                Logger.Info($"Chunked upload complete: {fileName} â†’ /v/{slug} (trace={responseTrace})");
                UploadResumeStore.Remove(sessionKey);
                return new UploadResult(true, slug, null, responseTrace);
            }

            Logger.Error($"Chunk complete failed ({(int)completeResp.StatusCode}): {completeText}");
            return new UploadResult(false, null, $"Complete failed: HTTP {(int)completeResp.StatusCode}", traceId, false, IsTransientStatusCode(completeResp.StatusCode), GetRetryAfter(completeResp));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warn("Chunk complete timed out");
            return new UploadResult(false, null, "Chunk completion timed out", traceId, false, true);
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Chunk complete network error", ex);
            return new UploadResult(false, null, $"Complete error: {ex.Message}", traceId, false, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Chunk complete error", ex);
            return new UploadResult(false, null, $"Complete error: {ex.Message}", traceId);
        }
    }

    private static async Task<ChunkInitResult> InitializeChunkedUploadAsync(
        string baseUrl,
        string apiToken,
        string fileName,
        string title,
        string mimeType,
        long totalSize,
        int totalChunks,
        bool preCompressed,
        string? expectedSha256,
        string? traceId,
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
            expectedSha256,
            traceId,
        });
        initReq.Content = new StringContent(initBody, System.Text.Encoding.UTF8, "application/json");
        using var initResp = await _http.SendAsync(initReq, ct);
        var initText = await initResp.Content.ReadAsStringAsync(ct);

        if (!initResp.IsSuccessStatusCode)
        {
            Logger.Error($"Chunk init failed ({(int)initResp.StatusCode}): {initText}");
            return new ChunkInitResult(
                "",
                $"Chunk init failed: HTTP {(int)initResp.StatusCode}",
                IsTransientStatusCode(initResp.StatusCode),
                GetRetryAfter(initResp));
        }

        using var doc = JsonDocument.Parse(initText);
        var uploadId = doc.RootElement.GetProperty("uploadId").GetString() ?? "";
        Logger.Debug($"Chunked upload initialized: {uploadId}");
        return new ChunkInitResult(uploadId);
    }

    private static async Task<string?> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to compute upload checksum for {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
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
            using var response = await _http.SendAsync(request, ct);
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

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        var numeric = (int)statusCode;
        return numeric == 408 || numeric == 425 || numeric == 429 || numeric >= 500;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return delta;

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var retryAfter = date - DateTimeOffset.UtcNow;
            if (retryAfter > TimeSpan.Zero)
                return retryAfter;
        }

        return null;
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
