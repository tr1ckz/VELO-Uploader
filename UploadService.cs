using System.Net.Http.Headers;

namespace VeloUploader;

public class UploadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };

    public record UploadResult(bool Success, string? Slug, string? Error);

    public static async Task<UploadResult> UploadAsync(
        string serverUrl, string apiToken, string filePath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists)
            return new UploadResult(false, null, "File not found");

        var fileName = fi.Name;
        var title = Path.GetFileNameWithoutExtension(fileName);

        // Try to detect game from parent folder name (ShadowPlay saves to Videos\<GameName>\)
        var parentDir = fi.Directory?.Name;
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (parentDir != null && !string.Equals(fi.DirectoryName, videosDir, StringComparison.OrdinalIgnoreCase))
        {
            title = $"{parentDir} - {title}";
        }

        var url = serverUrl.TrimEnd('/') + "/api/videos";

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var progressStream = new ProgressStream(fileStream, fi.Length, progress);
        using var content = new StreamContent(progressStream, 81920);

        var mimeType = fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4"
            : fileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ? "video/x-matroska"
            : fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm"
            : fileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime"
            : fileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ? "video/x-msvideo"
            : "video/mp4";

        content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Headers.ContentLength = fi.Length;

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        request.Headers.Add("X-Upload-Filename", Uri.EscapeDataString(fileName));
        request.Headers.Add("X-Upload-Title", Uri.EscapeDataString(title));

        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                // Parse slug from response
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var slug = doc.RootElement.GetProperty("slug").GetString();
                return new UploadResult(true, slug, null);
            }

            // Try to extract error message
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var error = doc.RootElement.GetProperty("error").GetString();
                return new UploadResult(false, null, error ?? $"HTTP {(int)response.StatusCode}");
            }
            catch
            {
                return new UploadResult(false, null, $"HTTP {(int)response.StatusCode}: {body}");
            }
        }
        catch (OperationCanceledException)
        {
            return new UploadResult(false, null, "Upload cancelled");
        }
        catch (Exception ex)
        {
            return new UploadResult(false, null, ex.Message);
        }
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
