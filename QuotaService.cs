using System.Text.Json;

namespace VeloUploader;

public enum QuotaFetchStatus
{
    Ok,
    NotConfigured,
    Unauthorized,
    ServerOutdated,
    NetworkError,
}

public record QuotaInfo(long UsedBytes, long? QuotaBytes)
{
    public bool HasQuota => QuotaBytes.HasValue;
    public long FreeBytes => HasQuota ? Math.Max(0, QuotaBytes!.Value - UsedBytes) : long.MaxValue;
    public bool WouldExceed(long fileSize) => HasQuota && (UsedBytes + fileSize > QuotaBytes!.Value);

    public string UsedFormatted => FormatBytes(UsedBytes);
    public string QuotaFormatted => HasQuota ? FormatBytes(QuotaBytes!.Value) : "Unlimited";
    public string FreeFormatted => HasQuota ? FormatBytes(FreeBytes) : "Unlimited";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }
}

public record QuotaFetchResult(QuotaFetchStatus Status, QuotaInfo? Quota, string? Message = null)
{
    public bool Success => Status == QuotaFetchStatus.Ok && Quota != null;
}

public static class QuotaService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static QuotaFetchResult? _cached;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Fetch storage quota from the server. Returns null on network error.
    /// Results are cached for 2 minutes.
    /// </summary>
    public static async Task<QuotaFetchResult> GetAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.ApiToken))
            return new QuotaFetchResult(QuotaFetchStatus.NotConfigured, null, "Uploader not configured");

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null && DateTime.UtcNow < _cacheExpiry)
                return _cached;

            using var handler = TlsCertHelper.CreateHandler(settings);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);

            var url = settings.ServerUrl.TrimEnd('/') + "/api/quota";
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"Quota check returned {(int)response.StatusCode}");
                var result = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => new QuotaFetchResult(QuotaFetchStatus.Unauthorized, null, "API token unauthorized"),
                    System.Net.HttpStatusCode.Forbidden => new QuotaFetchResult(QuotaFetchStatus.Unauthorized, null, "API token forbidden"),
                    System.Net.HttpStatusCode.NotFound => new QuotaFetchResult(QuotaFetchStatus.ServerOutdated, null, "Server missing /api/quota"),
                    _ => new QuotaFetchResult(QuotaFetchStatus.NetworkError, null, $"Server returned {(int)response.StatusCode}"),
                };
                _cached = result;
                _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
                return result;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var usedBytes = root.GetProperty("usedBytes").GetInt64();
            long? quotaBytes = null;
            if (root.GetProperty("quotaBytes").ValueKind != JsonValueKind.Null)
                quotaBytes = root.GetProperty("quotaBytes").GetInt64();

            _cached = new QuotaFetchResult(QuotaFetchStatus.Ok, new QuotaInfo(usedBytes, quotaBytes));
            _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
            return _cached;
        }
        catch (OperationCanceledException)
        {
            return new QuotaFetchResult(QuotaFetchStatus.NetworkError, null, "Quota check timed out");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Quota check failed: {ex.Message}");
            return new QuotaFetchResult(QuotaFetchStatus.NetworkError, null, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Invalidate the cache so the next call fetches fresh data.</summary>
    public static void Invalidate()
    {
        _lock.Wait();
        _cached = null;
        _lock.Release();
    }
}
